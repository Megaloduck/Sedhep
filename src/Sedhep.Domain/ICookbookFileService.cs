// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sedhep.Domain;

/// <summary>
/// Reads and writes portable <c>.cookbook</c> files. The format is versioned from the first release
/// so a file written today can still be read by a future version of the app.
/// </summary>
public interface ICookbookFileService
{
    /// <summary>
    /// Writes a cookbook package containing the given recipes (all of them when
    /// <paramref name="recipeIds"/> is null) and the photos they reference.
    /// </summary>
    Task<CookbookFileSummary> WriteAsync(
        Stream destination,
        IReadOnlyCollection<Guid>? recipeIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a cookbook package and adds its recipes to the local cookbook. When a recipe exists in
    /// both places, <paramref name="policy"/> decides which copy survives; without one the local
    /// copy is always kept, because the plan says an import must never silently destroy it.
    /// </summary>
    Task<CookbookImportResult> ReadAsync(
        Stream source,
        ICookbookImportPolicy? policy = null,
        CancellationToken cancellationToken = default);
}

public sealed record CookbookFileSummary(int RecipeCount, int PhotoCount, long Bytes);

public sealed record CookbookImportResult(
    int Imported,
    int Replaced,
    int PhotosRestored,
    int Skipped,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Recipes that existed on both sides, whether or not the user resolved them.</summary>
    public IReadOnlyList<CookbookConflict> Conflicts { get; init; } = Array.Empty<CookbookConflict>();
}

/// <summary>Two copies of the same recipe, and no obvious winner.</summary>
public sealed record CookbookConflict(Guid RecipeId, string Title, int LocalRevision, int IncomingRevision);

public enum ConflictDecision
{
    /// <summary>Leave the recipe on this device exactly as it is.</summary>
    KeepLocal,

    /// <summary>Overwrite the local copy with the one from the package.</summary>
    UseIncoming
}

/// <summary>
/// Asked once per recipe that exists in both cookbooks. The UI answers this; an import that has no
/// policy keeps the local copy.
/// </summary>
public interface ICookbookImportPolicy
{
    Task<ConflictDecision> ResolveAsync(CookbookConflict conflict, CancellationToken cancellationToken = default);
}
