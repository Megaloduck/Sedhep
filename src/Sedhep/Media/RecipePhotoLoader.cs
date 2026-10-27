// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;

namespace Sedhep.Media;

public sealed class RecipePhotoLoader : IRecipePhotoLoader
{
    private readonly IRecipePhotoStorage _storage;
    private readonly ILogger<RecipePhotoLoader> _logger;

    public RecipePhotoLoader(IRecipePhotoStorage storage, ILogger<RecipePhotoLoader> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<Bitmap?> LoadAsync(RecipePhoto photo, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var source = await _storage.OpenReadAsync(
                photo.RecipeId.ToString(),
                photo.StorageId,
                cancellationToken);

            if (source is null)
            {
                _logger.LogWarning("Photo {StorageId} is missing from local storage", photo.StorageId);
                return null;
            }

            // Decode from a copy so the file handle can be released immediately.
            using var buffered = new MemoryStream();
            await source.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;
            return new Bitmap(buffered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read photo {StorageId}", photo.StorageId);
            return null;
        }
    }
}
