// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sedhep.Domain;

public interface IRecipePhotoStorage
{
    string DefaultPhotosDirectory { get; }
    Task<string> StoreAsync(string recipeId, string id, Stream source, string? hash = null, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string recipeId, string id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string recipeId, string id, CancellationToken cancellationToken = default);
}
