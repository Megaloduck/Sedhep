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

    /// <summary>Reads a cookbook package and adds its recipes to the local cookbook.</summary>
    Task<CookbookImportResult> ReadAsync(Stream source, CancellationToken cancellationToken = default);
}

public sealed record CookbookFileSummary(int RecipeCount, int PhotoCount, long Bytes);

public sealed record CookbookImportResult(
    int Imported,
    int Replaced,
    int PhotosRestored,
    int Skipped,
    IReadOnlyList<string> Warnings);
