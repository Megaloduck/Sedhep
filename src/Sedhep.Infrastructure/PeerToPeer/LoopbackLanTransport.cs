// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;

namespace Sedhep.Infrastructure;

public interface ILocalNetworkTransport : IAsyncDisposable
{
    IReadOnlyList<string> DiscoveredDeviceIds { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task DeliverToDeviceAsync(string targetDeviceId, string payloadId, Stream payload, string? payloadHash, DateTimeOffset offeredAt, CancellationToken cancellationToken = default);
    Task<Stream?> OpenPayloadStreamAsync(string sourceDeviceId, string payloadId, CancellationToken cancellationToken = default);
    event Action<string>? DeviceDiscovered;
    event Action<string>? DeviceLost;
}

public sealed class LoopbackLanTransport : ILocalNetworkTransport
{
    private readonly LoopbackOfferCollector _collector;
    private readonly LoopbackLanDiscovery _discovery;
    private readonly ILogger<LoopbackLanTransport> _logger;
    private readonly IRecipePhotoStorage _photos;
    private readonly IClock _clock;
    private readonly HashSet<string> _peers = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private bool _started;

    public IReadOnlyList<string> DiscoveredDeviceIds
    {
        get
        {
            lock (_lock) return _peers.ToList().AsReadOnly();
        }
    }

    public event Action<string>? DeviceDiscovered;
    public event Action<string>? DeviceLost;

    public LoopbackLanTransport(
        LoopbackOfferCollector collector,
        LoopbackLanDiscovery discovery,
        ILogger<LoopbackLanTransport> logger,
        IRecipePhotoStorage photos,
        IClock clock)
    {
        _collector = collector;
        _discovery = discovery;
        _logger = logger;
        _photos = photos;
        _clock = clock;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return Task.CompletedTask;
        _started = true;

        _discovery.DeviceFound += OnDeviceFound;
        _discovery.DeviceLost += OnDeviceLost;
        _ = _discovery.StartDiscoveryLoopAsync(cancellationToken);

        _logger.LogInformation(
            "LoopbackLAN transport started on device {DeviceId}",
            _collector.ThisDeviceId);
        return Task.CompletedTask;
    }

    public Task DeliverToDeviceAsync(
        string targetDeviceId,
        string payloadId,
        Stream payload,
        string? payloadHash,
        DateTimeOffset offeredAt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "DeliverToDeviceAsync: target={TargetDeviceId} payload={PayloadId}",
            targetDeviceId,
            payloadId);

        if (string.Equals(targetDeviceId, _collector.ThisDeviceId, StringComparison.Ordinal))
        {
            try
            {
                using var ms = new MemoryStream();
                payload.CopyTo(ms);
                ms.Position = 0;

                // Placeholder: real delivery would hand off to a repository service.
                _logger.LogInformation(
                    "Loopback delivery stub: stored placeholder for payload {PayloadId}",
                    payloadId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loopback delivery failed for self-target");
            }
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> OpenPayloadStreamAsync(
        string sourceDeviceId,
        string payloadId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "OpenPayloadStream requested: source={SourceDeviceId} payload={PayloadId}",
            sourceDeviceId,
            payloadId);
        return Task.FromResult<Stream?>(null);
    }

    private void OnDeviceFound(string deviceId)
    {
        lock (_lock)
        {
            if (!_peers.Add(deviceId)) return;
        }
        DeviceDiscovered?.Invoke(deviceId);
        _logger.LogInformation("Peer discovered: {DeviceId}", deviceId);
    }

    private void OnDeviceLost(string deviceId)
    {
        lock (_lock)
        {
            if (!_peers.Remove(deviceId)) return;
        }
        DeviceLost?.Invoke(deviceId);
        _logger.LogInformation("Peer lost: {DeviceId}", deviceId);
    }

    public async ValueTask DisposeAsync()
    {
        _discovery.DeviceFound -= OnDeviceFound;
        _discovery.DeviceLost -= OnDeviceLost;
        await _discovery.DisposeAsync();
        _started = false;
    }
}

public sealed class LoopbackLanDiscovery : IAsyncDisposable
{
    private readonly LoopbackOfferCollector _collector;
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer _loop;

    public event Action<string>? DeviceFound;
    public event Action<string>? DeviceLost;

    public LoopbackLanDiscovery(LoopbackOfferCollector collector)
    {
        _collector = collector;
        _loop = new Timer(_ => _ = ScanAsync(), null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
    }

    private async Task ScanAsync()
    {
        if (_cts.IsCancellationRequested) return;
        try
        {
            var localId = _collector.ThisDeviceId;
            var peers = await _collector.ListPeersAsync(_cts.Token);
            foreach (var peer in peers)
            {
                if (string.Equals(peer, localId, StringComparison.Ordinal)) continue;
                DeviceFound?.Invoke(peer);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // During startup the collector may not be fully ready; tolerate it.
        }
    }

    public Task StartDiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _loop.Dispose();
        return default;
    }
}

public sealed class LoopbackOfferCollector
{
    private readonly string _localDeviceId;
    private readonly List<string> _knownPeers = new() { "peer-one" };

    public string ThisDeviceId => _localDeviceId;

    public LoopbackOfferCollector()
    {
        _localDeviceId = Environment.GetEnvironmentVariable("SEDHEP_DEVICE_ID")
                            ?? Guid.NewGuid().ToString("N");
    }

    public Task<IReadOnlyList<string>> ListPeersAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>(_knownPeers.ToList());
    }
}
