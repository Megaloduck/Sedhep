// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sedhep.Domain;
using Microsoft.Extensions.Logging;

namespace Sedhep.Infrastructure;

public sealed class FileSystemRecipePhotoStorage : IRecipePhotoStorage
{
    private readonly string _root;
    private readonly ILogger<FileSystemRecipePhotoStorage> _logger;

    public FileSystemRecipePhotoStorage(string root, ILogger<FileSystemRecipePhotoStorage> logger)
    {
        _root = root;
        _logger = logger;
        var dir = new DirectoryInfo(root);
        if (!dir.Exists) dir.Create();
    }

    public string DefaultPhotosDirectory => _root;

    public async Task<string> StoreAsync(string recipeId, string id, Stream source, string? hash = null, CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(_root, recipeId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{id}.jpg");
        _logger.LogDebug("Storing photo at {Path}", path);
        await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await source.CopyToAsync(fs, cancellationToken);
        }
        return path;
    }

    public async Task<Stream?> OpenReadAsync(string recipeId, string id, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, recipeId, $"{id}.jpg");
        if (!File.Exists(path)) return null;
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
    }

    public Task<bool> ExistsAsync(string recipeId, string id, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, recipeId, $"{id}.jpg");
        return Task.FromResult(File.Exists(path));
    }
}
