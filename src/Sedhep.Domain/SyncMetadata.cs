// Licensed under the MIT license. See LICENSE in the project root.

using System;

namespace Sedhep.Domain;

public sealed class SyncMetadata
{
    public Guid Id { get; set; }
    public string LocalDeviceId { get; set; } = "";
    public string RemoteDeviceId { get; set; } = "";
    public string RecipeId { get; set; } = "";
    public DateTimeOffset OfferedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? PayloadHash { get; set; }
    public int PayloadBytes { get; set; }
}
