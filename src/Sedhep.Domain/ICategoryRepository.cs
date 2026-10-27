// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sedhep.Domain;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a category. Recipes that were filed under it are kept and become uncategorised.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountRecipesAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
