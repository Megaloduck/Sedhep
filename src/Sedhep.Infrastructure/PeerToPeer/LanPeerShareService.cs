// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;

namespace Sedhep.Infrastructure;

/// <summary>
/// Finds other Sedhep devices on the same network and moves cookbook packages between them.
/// </summary>
/// <remarks>
/// Discovery is a small multicast heartbeat; the transfer itself is a direct TCP connection that is
/// encrypted with a key both sides derive from an ephemeral Diffie-Hellman exchange. Nothing is
/// accepted without the person holding the receiving device saying yes.
/// </remarks>
public sealed class LanPeerShareService : INearbyShareService
{
    private const int DiscoveryPort = 43211;
    private const string ProtocolTag = "sedhep-v1";
    private const int MaxFrameBytes = 4 * 1024 * 1024;
    private const int ChunkBytes = 64 * 1024;

    private static readonly IPAddress GroupAddress = IPAddress.Parse("239.255.42.99");
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(9);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppSettingsStore _settings;
    private readonly ICookbookFileService _cookbookFiles;
    private readonly FileSystemAppSandbox _sandbox;
    private readonly ILogger<LanPeerShareService> _logger;

    private readonly object _gate = new();
    private readonly Dictionary<string, PeerRecord> _peers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingOffer> _pending = new(StringComparer.Ordinal);

    private UdpClient? _udp;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _listenPort;

    public LanPeerShareService(
        AppSettingsStore settings,
        ICookbookFileService cookbookFiles,
        FileSystemAppSandbox sandbox,
        ILogger<LanPeerShareService> logger)
    {
        _settings = settings;
        _cookbookFiles = cookbookFiles;
        _sandbox = sandbox;
        _logger = logger;
    }

    public string ThisDeviceName => _settings.Current.DeviceName;

    public bool IsDiscovering => _cts is { IsCancellationRequested: false };

    public IReadOnlyList<NearbyDevice> Devices
    {
        get
        {
            lock (_gate)
            {
                return _peers.Values
                    .OrderBy(p => p.Device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(p => p.Device)
                    .ToList();
            }
        }
    }

    public event EventHandler? DevicesChanged;

    public event EventHandler<IncomingShareOffer>? OfferReceived;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_cts is { IsCancellationRequested: false })
            {
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        var token = _cts!.Token;

        try
        {
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            _listenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            JoinGroup(_udp);
            _udp.MulticastLoopback = true;

            _ = Task.Run(() => DiscoveryLoopAsync(token), token);
            _ = Task.Run(() => AcceptLoopAsync(token), token);
            _ = Task.Run(() => HeartbeatLoopAsync(token), token);

            _logger.LogInformation(
                "Nearby sharing ready as {Name} on port {Port}",
                ThisDeviceName,
                _listenPort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start nearby sharing");
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    private void JoinGroup(UdpClient udp)
    {
        var joined = 0;

        // Joining per interface keeps discovery working on machines with Wi-Fi and Ethernet at once,
        // and lets two copies of the app find each other on the same machine.
        foreach (var address in LocalAddresses())
        {
            try
            {
                udp.JoinMulticastGroup(GroupAddress, address);
                joined++;
            }
            catch (SocketException)
            {
                // Some adapters (VPN, virtual) refuse multicast; that is not fatal.
            }
        }

        if (joined == 0)
        {
            udp.JoinMulticastGroup(GroupAddress);
        }
    }

    private static IEnumerable<IPAddress> LocalAddresses()
    {
        var addresses = new List<IPAddress> { IPAddress.Loopback };

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(info.Address);
                    }
                }
            }
        }
        catch
        {
            // Interface enumeration is best effort.
        }

        return addresses;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            await SendAnnounceAsync("bye", CancellationToken.None);
        }
        catch
        {
            // Leaving quietly is fine.
        }

        await cts.CancelAsync();

        _udp?.Dispose();
        _udp = null;
        _listener?.Stop();
        _listener = null;

        lock (_gate)
        {
            _peers.Clear();
            foreach (var pending in _pending.Values)
            {
                pending.Answer.TrySetResult(false);
            }

            _pending.Clear();
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
        cts.Dispose();
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                await SendAnnounceAsync("hello", token);
                ExpirePeers();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Heartbeat stopped");
        }
    }

    private async Task SendAnnounceAsync(string kind, CancellationToken token)
    {
        var udp = _udp;
        if (udp is null) return;

        var message = JsonSerializer.SerializeToUtf8Bytes(new Announcement
        {
            Kind = kind,
            DeviceId = _settings.Current.DeviceId,
            DeviceName = _settings.Current.DeviceName,
            Port = _listenPort
        }, JsonOptions);

        await udp.SendAsync(message, new IPEndPoint(GroupAddress, DiscoveryPort), token);
    }

    private void ExpirePeers()
    {
        var changed = false;
        var cutoff = DateTimeOffset.UtcNow - PeerTimeout;

        lock (_gate)
        {
            foreach (var id in _peers.Where(p => p.Value.LastSeen < cutoff).Select(p => p.Key).ToList())
            {
                _peers.Remove(id);
                changed = true;
            }
        }

        if (changed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task DiscoveryLoopAsync(CancellationToken token)
    {
        var udp = _udp;
        if (udp is null) return;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(token);
                var announcement = JsonSerializer.Deserialize<Announcement>(received.Buffer, JsonOptions);
                if (announcement is null || string.IsNullOrEmpty(announcement.DeviceId))
                {
                    continue;
                }

                if (string.Equals(announcement.DeviceId, _settings.Current.DeviceId, StringComparison.Ordinal))
                {
                    continue;
                }

                var address = received.RemoteEndPoint.Address.ToString();
                NotePeer(announcement, address, received.RemoteEndPoint.Address);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Discovery socket hiccup");
                await Task.Delay(500, token);
            }
        }
    }

    private void NotePeer(Announcement announcement, string address, IPAddress remote)
    {
        var changed = false;

        lock (_gate)
        {
            if (string.Equals(announcement.Kind, "bye", StringComparison.Ordinal))
            {
                changed = _peers.Remove(announcement.DeviceId);
            }
            else if (announcement.Port > 0)
            {
                // Loopback announcements arrive from a virtual endpoint; prefer the loopback
                // address in that case so two apps on one machine can talk to each other.
                var host = IPAddress.IsLoopback(remote) ? IPAddress.Loopback.ToString() : address;
                var device = new NearbyDevice(announcement.DeviceId, announcement.DeviceName, host, announcement.Port);
                var isNew = !_peers.ContainsKey(announcement.DeviceId);
                _peers[announcement.DeviceId] = new PeerRecord(device, DateTimeOffset.UtcNow);
                changed = isNew;
            }
        }

        if (changed)
        {
            _logger.LogInformation("Nearby device {Device} at {Address}:{Port}",
                announcement.DeviceName, address, announcement.Port);
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        var listener = _listener;
        if (listener is null) return;

        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Accept failed");
                continue;
            }

            _ = Task.Run(() => HandleIncomingAsync(client, token), token);
        }
    }

    private async Task HandleIncomingAsync(TcpClient client, CancellationToken token)
    {
        var transferId = Guid.NewGuid().ToString("N");
        var senderName = "Another phone";
        PendingOffer? pending = null;

        try
        {
            client.NoDelay = true;
            await using var stream = client.GetStream();

            var hello = await FrameChannel.ReadPlainJsonAsync(stream, token);
            if (hello is null || hello.Type != "hello" || string.IsNullOrEmpty(hello.PublicKey))
            {
                return;
            }

            senderName = string.IsNullOrWhiteSpace(hello.DeviceName) ? senderName : hello.DeviceName!;

            using var own = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            await FrameChannel.WritePlainJsonAsync(stream, new ControlMessage
            {
                Type = "hello-ack",
                DeviceId = _settings.Current.DeviceId,
                DeviceName = _settings.Current.DeviceName,
                PublicKey = Convert.ToBase64String(own.ExportSubjectPublicKeyInfo())
            }, token);

            using var channel = FrameChannel.Establish(
                stream,
                own,
                Convert.FromBase64String(hello.PublicKey!),
                isServer: true);

            var offer = await channel.ReadJsonAsync(token);
            if (offer is null || offer.Type != "offer")
            {
                return;
            }

            var incoming = new IncomingShareOffer
            {
                TransferId = transferId,
                DeviceId = hello.DeviceId ?? "",
                DeviceName = senderName,
                RecipeCount = offer.RecipeCount,
                TotalBytes = offer.TotalBytes,
                Summary = offer.Summary ?? "A cookbook"
            };

            pending = new PendingOffer();
            lock (_gate)
            {
                _pending[transferId] = pending;
            }

            OfferReceived?.Invoke(this, incoming);

            var accepted = await pending.Answer.Task.WaitAsync(TimeSpan.FromMinutes(2), token);
            lock (_gate)
            {
                _pending.Remove(transferId);
            }

            await channel.WriteJsonAsync(new ControlMessage
            {
                Type = "offer-answer",
                Accepted = accepted,
                Message = accepted ? null : "The other device declined."
            }, token);

            if (!accepted)
            {
                pending.Completed.TrySetResult(new ShareResult(false, "Declined"));
                _logger.LogInformation("Declined transfer from {Device}", senderName);
                return;
            }

            // A cookbook with thousands of photos is far too big to hold in memory, so the package
            // lands on disk next to the cookbook and is deleted once it has been read.
            var incomingDirectory = Path.Combine(_sandbox.RootDirectory, "incoming");
            Directory.CreateDirectory(incomingDirectory);
            var packagePath = Path.Combine(incomingDirectory, $"{transferId}.cookbook");
            long total = 0;

            try
            {
                await using (var file = new FileStream(
                                 packagePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    while (true)
                    {
                        var frame = await channel.ReadLineAsync(token);
                        if (frame is null)
                        {
                            return;
                        }

                        if (frame.Value.Kind == FrameKind.End)
                        {
                            break;
                        }

                        total += frame.Value.Payload.Length;
                        await file.WriteAsync(frame.Value.Payload, token);
                    }
                }

                await using var received = File.OpenRead(packagePath);
                var result = await _cookbookFiles.ReadAsync(received, null, token);

                _logger.LogInformation(
                    "Received {Recipes} recipes ({Bytes} bytes) from {Device}",
                    result.Imported + result.Replaced, total, senderName);

                var receivedCount = result.Imported + result.Replaced;
                var message = receivedCount > 0
                    ? $"{receivedCount} recipes received from {senderName}"
                    : $"Nothing new arrived from {senderName}";

                pending.Completed.TrySetResult(new ShareResult(true, message, receivedCount, result.PhotosRestored));

                await channel.WriteJsonAsync(new ControlMessage
                {
                    Type = "result",
                    Success = true,
                    Message = message,
                    RecipeCount = receivedCount,
                    PhotosRestored = result.PhotosRestored
                }, token);
            }
            finally
            {
                TryDelete(packagePath);
            }
        }
        catch (TimeoutException)
        {
            pending?.Completed.TrySetResult(new ShareResult(false, "The other device did not answer."));
            _logger.LogInformation("Transfer from {Device} timed out waiting for an answer", senderName);
        }
        catch (Exception ex)
        {
            pending?.Completed.TrySetResult(new ShareResult(false, "The transfer was interrupted."));
            _logger.LogError(ex, "Incoming transfer from {Device} failed", senderName);
        }
        finally
        {
            client.Dispose();
        }
    }

    public async Task<ShareResult> SendAsync(
        NearbyDevice device,
        Stream package,
        string summary,
        int recipeCount,
        long totalBytes,
        IProgress<ShareProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        client.NoDelay = true;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            await client.ConnectAsync(device.Address, device.Port, timeout.Token);

            await using var stream = client.GetStream();

            using var own = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            await FrameChannel.WritePlainJsonAsync(stream, new ControlMessage
            {
                Type = "hello",
                DeviceId = _settings.Current.DeviceId,
                DeviceName = _settings.Current.DeviceName,
                PublicKey = Convert.ToBase64String(own.ExportSubjectPublicKeyInfo())
            }, cancellationToken);

            var ack = await FrameChannel.ReadPlainJsonAsync(stream, cancellationToken);
            if (ack is null || string.IsNullOrEmpty(ack.PublicKey))
            {
                return new ShareResult(false, "That device did not answer.");
            }

            using var channel = FrameChannel.Establish(
                stream,
                own,
                Convert.FromBase64String(ack.PublicKey!),
                isServer: false);

            progress?.Report(new ShareProgress(5, $"Waiting for {device.DisplayName} to accept…"));

            await channel.WriteJsonAsync(new ControlMessage
            {
                Type = "offer",
                TransferId = Guid.NewGuid().ToString("N"),
                Summary = summary,
                RecipeCount = recipeCount,
                TotalBytes = totalBytes
            }, cancellationToken);

            // The person on the other device has to look at the offer and decide; give them time,
            // but do not hold the connection open forever.
            using var answerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            answerTimeout.CancelAfter(TimeSpan.FromMinutes(2));

            var answer = await channel.ReadJsonAsync(answerTimeout.Token);
            if (answer is null || !answer.Accepted)
            {
                return new ShareResult(false, answer?.Message ?? $"{device.DisplayName} declined.");
            }

            var buffer = new byte[ChunkBytes];
            long sent = 0;
            var lastReport = 5;

            while (true)
            {
                var read = await package.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                await channel.WriteChunkAsync(buffer.AsMemory(0, read), cancellationToken);
                sent += read;

                var percent = totalBytes > 0
                    ? (int)Math.Clamp(5 + sent * 90 / totalBytes, 5, 95)
                    : 50;

                if (percent > lastReport)
                {
                    lastReport = percent;
                    progress?.Report(new ShareProgress(percent, $"Sending… {percent}%"));
                }
            }

            await channel.WriteEndAsync(cancellationToken);

            var result = await channel.ReadJsonAsync(cancellationToken);
            if (result is null)
            {
                return new ShareResult(false, "The other device closed the connection.");
            }

            progress?.Report(new ShareProgress(100, result.Message ?? "Done"));
            return new ShareResult(
                result.Success,
                result.Message ?? (result.Success ? "Sent" : "The transfer failed."),
                result.RecipeCount,
                result.PhotosRestored);
        }
        catch (OperationCanceledException)
        {
            return new ShareResult(false, "The transfer was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sending to {Device} failed", device.DisplayName);
            return new ShareResult(false, $"Could not reach {device.DisplayName}.");
        }
    }

    public async Task<ShareResult> RespondAsync(string transferId, bool accepted, CancellationToken cancellationToken = default)
    {
        PendingOffer? pending;
        lock (_gate)
        {
            _pending.TryGetValue(transferId, out pending);
        }

        if (pending is null)
        {
            return new ShareResult(false, "That transfer is no longer waiting.");
        }

        pending.Answer.TrySetResult(accepted);

        if (!accepted)
        {
            return new ShareResult(false, "Declined");
        }

        // Accepting only starts the transfer; the UI shows the real outcome once the recipes are in.
        try
        {
            return await pending.Completed.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        }
        catch (TimeoutException)
        {
            return new ShareResult(false, "The transfer took too long.");
        }
        catch (OperationCanceledException)
        {
            return new ShareResult(false, "The transfer was cancelled.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private sealed record PeerRecord(NearbyDevice Device, DateTimeOffset LastSeen);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover package is harmless; it is overwritten by the next transfer id.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class PendingOffer
    {
        public TaskCompletionSource<bool> Answer { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once the package has been received and imported on this device.</summary>
        public TaskCompletionSource<ShareResult> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Announcement
    {
        public string Kind { get; set; } = "hello";
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public int Port { get; set; }
    }

    private sealed class ControlMessage
    {
        public string Type { get; set; } = "";
        public string? DeviceId { get; set; }
        public string? DeviceName { get; set; }
        public string? PublicKey { get; set; }
        public string? TransferId { get; set; }
        public string? Summary { get; set; }
        public int RecipeCount { get; set; }
        public long TotalBytes { get; set; }
        public bool Accepted { get; set; }
        public bool Success { get; set; }
        public string? Message { get; set; }
        public int PhotosRestored { get; set; }
    }

    private enum FrameKind : byte
    {
        Json = 1,
        Chunk = 2,
        End = 3
    }

    /// <summary>
    /// A framed, encrypted view over the TCP stream. Every frame after the handshake is sealed with
    /// AES-GCM using a key that only these two devices can derive.
    /// </summary>
    private sealed class FrameChannel : IDisposable
    {
        private readonly Stream _stream;
        private readonly AesGcm _send;
        private readonly AesGcm _receive;

        private FrameChannel(Stream stream, AesGcm send, AesGcm receive)
        {
            _stream = stream;
            _send = send;
            _receive = receive;
        }

        public static FrameChannel Establish(
            Stream stream,
            ECDiffieHellman own,
            byte[] otherPublicKey,
            bool isServer)
        {
            using var other = ECDiffieHellman.Create();
            other.ImportSubjectPublicKeyInfo(otherPublicKey, out _);

            var secret = own.DeriveKeyMaterial(other.PublicKey);
            var salt = Encoding.UTF8.GetBytes(ProtocolTag);

            var clientToServer = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt, "c2s"u8.ToArray());
            var serverToClient = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt, "s2c"u8.ToArray());

            return isServer
                ? new FrameChannel(stream, new AesGcm(serverToClient, 16), new AesGcm(clientToServer, 16))
                : new FrameChannel(stream, new AesGcm(clientToServer, 16), new AesGcm(serverToClient, 16));
        }

        public static async Task WritePlainJsonAsync(Stream stream, ControlMessage message, CancellationToken token)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
            var header = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(payload, token);
            await stream.FlushAsync(token);
        }

        public static async Task<ControlMessage?> ReadPlainJsonAsync(Stream stream, CancellationToken token)
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, token);
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header);
            if (length is <= 0 or > MaxFrameBytes)
            {
                return null;
            }

            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, token);
            return JsonSerializer.Deserialize<ControlMessage>(payload, JsonOptions);
        }

        public Task WriteJsonAsync(ControlMessage message, CancellationToken token)
            => WriteFrameAsync(FrameKind.Json, JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions), token);

        public Task WriteChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken token)
            => WriteFrameAsync(FrameKind.Chunk, chunk, token);

        public Task WriteEndAsync(CancellationToken token)
            => WriteFrameAsync(FrameKind.End, ReadOnlyMemory<byte>.Empty, token);

        private async Task WriteFrameAsync(FrameKind kind, ReadOnlyMemory<byte> payload, CancellationToken token)
        {
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            Span<byte> aad = stackalloc byte[1];
            aad[0] = (byte)kind;

            var cipher = new byte[payload.Length + 16];
            _send.Encrypt(nonce, payload.Span, cipher.AsSpan(0, payload.Length), cipher.AsSpan(payload.Length), aad);

            var total = 1 + 12 + cipher.Length;
            var buffer = new byte[4 + total];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer, total);
            buffer[4] = (byte)kind;
            nonce.CopyTo(buffer, 5);
            cipher.CopyTo(buffer, 17);

            await _stream.WriteAsync(buffer, token);
            await _stream.FlushAsync(token);
        }

        public async Task<(FrameKind Kind, byte[] Payload)?> ReadLineAsync(CancellationToken token)
        {
            var header = new byte[4];
            await _stream.ReadExactlyAsync(header, token);
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header);
            if (length is < 17 or > MaxFrameBytes)
            {
                return null;
            }

            var frame = new byte[length];
            await _stream.ReadExactlyAsync(frame, token);

            var kind = (FrameKind)frame[0];
            var nonce = frame.AsSpan(1, 12);
            var cipher = frame.AsSpan(13);
            var plain = new byte[cipher.Length - 16];

            Span<byte> aad = stackalloc byte[1];
            aad[0] = (byte)kind;

            _receive.Decrypt(nonce, cipher[..^16], cipher[^16..], plain, aad);
            return (kind, plain);
        }

        public async Task<ControlMessage?> ReadJsonAsync(CancellationToken token)
        {
            var frame = await ReadLineAsync(token);
            if (frame is null || frame.Value.Kind != FrameKind.Json)
            {
                return null;
            }

            return JsonSerializer.Deserialize<ControlMessage>(frame.Value.Payload, JsonOptions);
        }

        public void Dispose()
        {
            _send.Dispose();
            _receive.Dispose();
        }
    }
}
