// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sedhep.Domain;

public interface IRecipeRepository
{
    Task<IReadOnlyList<Recipe>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Recipe?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Recipe>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Recipe>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Recipe>> GetFavoritesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Recipe>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
    Task SaveAsync(Recipe recipe, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
