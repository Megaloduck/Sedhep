// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;

namespace Sedhep.Infrastructure;

/// <summary>
/// Copies <see cref="SampleCookbook"/> into the cookbook the first time Sedhep runs, and never
/// again. The fact that it happened is remembered in the settings file rather than inferred from an
/// empty cookbook, so a cook who deletes the samples keeps them deleted.
/// </summary>
public sealed class SampleCookbookSeeder
{
    private readonly IRecipeRepository _recipes;
    private readonly ICategoryRepository _categories;
    private readonly AppSettingsStore _settings;
    private readonly IClock _clock;
    private readonly ILogger<SampleCookbookSeeder> _logger;

    public SampleCookbookSeeder(
        IRecipeRepository recipes,
        ICategoryRepository categories,
        AppSettingsStore settings,
        IClock clock,
        ILogger<SampleCookbookSeeder> logger)
    {
        _recipes = recipes;
        _categories = categories;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Adds the samples if they have never been added and the cookbook holds nothing the cook
    /// wrote. Returns true when recipes were inserted. Failing to seed is not a reason to start
    /// the app in a broken state, so callers may let exceptions travel to their own log.
    /// </summary>
    public async Task<bool> TrySeedAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        if (settings.SamplesAdded)
        {
            return false;
        }

        var existing = await _recipes.GetAllAsync(cancellationToken);

        // A cook who already has recipes is not shown samples: their cookbook is not empty, and
        // six unfamiliar dishes would only be in the way. The flag is set either way so the
        // question is never asked again.
        if (existing.Any(HasCookWrittenContent))
        {
            _logger.LogInformation("Sample recipes skipped: the cookbook already has recipes");
            MarkAdded();
            return false;
        }

        var categories = await _categories.GetAllAsync(cancellationToken);
        var samples = SampleCookbook.Create(categories, settings.DeviceId, _clock.UtcNow);

        foreach (var recipe in samples)
        {
            await _recipes.SaveAsync(recipe, cancellationToken);
        }

        MarkAdded();
        _logger.LogInformation("Added {Count} sample recipes to the cookbook", samples.Count);
        return true;
    }

    /// <summary>
    /// Placeholders left behind by an abandoned \"new recipe\" screen have no steps and no
    /// description; those must not stop the samples from being added.
    /// </summary>
    private static bool HasCookWrittenContent(Recipe recipe)
    {
        return recipe.Steps.Count > 0
               || recipe.Ingredients.Count > 1
               || !string.IsNullOrWhiteSpace(recipe.Description)
               || !string.IsNullOrWhiteSpace(recipe.Notes);
    }

    private void MarkAdded()
    {
        var current = _settings.Current;
        current.SamplesAdded = true;
        _settings.Save(current);
    }
}
