// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sedhep.Domain;

/// <summary>
/// A device that is close enough to exchange recipes with, found on the local network.
/// </summary>
public sealed record NearbyDevice(string DeviceId, string Name, string Address, int Port)
{
    /// <summary>What the user sees; never an IP address.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Another phone" : Name;
}

/// <summary>An offer that arrived from a nearby device and is waiting for the user to decide.</summary>
public sealed class IncomingShareOffer
{
    public required string TransferId { get; init; }
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public required int RecipeCount { get; init; }
    public required long TotalBytes { get; init; }
    public required string Summary { get; init; }
    public string? Message { get; set; }
}

public sealed record ShareProgress(int Percent, string Message);

/// <summary>
/// Direct device-to-device transfer. Deliberately separate from the cookbook domain: the transport
/// only moves an encrypted package, and the receiving side decides what to do with it.
/// </summary>
public interface INearbyShareService : IAsyncDisposable
{
    /// <summary>The name this device presents to others, e.g. "Rina's phone".</summary>
    string ThisDeviceName { get; }

    bool IsDiscovering { get; }

    IReadOnlyList<NearbyDevice> Devices { get; }

    event EventHandler? DevicesChanged;

    /// <summary>An offer from another device is waiting for an answer.</summary>
    event EventHandler<IncomingShareOffer>? OfferReceived;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>Sends the given package to a nearby device and waits for its answer.</summary>
    Task<ShareResult> SendAsync(
        NearbyDevice device,
        Stream package,
        string summary,
        int recipeCount,
        long totalBytes,
        IProgress<ShareProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Answers a pending offer; accepting starts the transfer.</summary>
    Task<ShareResult> RespondAsync(string transferId, bool accepted, CancellationToken cancellationToken = default);
}

public sealed record ShareResult(bool Success, string Message, int RecipeCount = 0, int PhotosRestored = 0);
