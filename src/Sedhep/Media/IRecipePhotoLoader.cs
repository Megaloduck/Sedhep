// Licensed under the MIT license. See LICENSE in the project root.

using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Sedhep.Domain;

namespace Sedhep.Media;

/// <summary>
/// Turns a stored photo into a bitmap the UI can show. Keeping this behind a port means the view
/// models never touch the file system, and only the recipe being looked at is ever decoded.
/// </summary>
public interface IRecipePhotoLoader
{
    Task<Bitmap?> LoadAsync(RecipePhoto photo, CancellationToken cancellationToken = default);
}
