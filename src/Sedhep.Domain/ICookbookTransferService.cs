// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sedhep.Domain;

public interface ICookbookTransferService
{
    Task DeliverToLocalRepositoryAsync(
        string sourceDeviceId,
        string payloadId,
        Stream payload,
        string? payloadHash,
        DateTimeOffset offeredAt,
        CancellationToken cancellationToken = default);

    Task<Stream> ExportCookbookAsync(
        string exportTag,
        IReadOnlyCollection<string> recipeIds,
        IProgress<CookbookExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record CookbookExportProgress(
    string ExportTag,
    int ReceivedRecipeCount,
    long ReceivedPhotoBytes,
    int TotalRecipeCount,
    long TotalPhotoBytes);
