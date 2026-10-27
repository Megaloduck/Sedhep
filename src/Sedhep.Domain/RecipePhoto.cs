// Licensed under the MIT license. See LICENSE in the project root.

using System;

namespace Sedhep.Domain;

public sealed class RecipePhoto
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public string StorageId { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? Hash { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public bool IsCover { get; set; }
}
