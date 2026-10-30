using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Sedhep.Domain;
using Sedhep.Infrastructure;
using Xunit;

namespace Sedhep.Tests;

public sealed class SampleCookbookTests
{
    private static SampleCookbookSeeder Seeder(TestCookbook cookbook) => new(
        cookbook.Recipes,
        cookbook.Categories,
        cookbook.Settings,
        new UtcClock(),
        NullLogger<SampleCookbookSeeder>.Instance);

    [Fact]
    public async Task An_empty_cookbook_starts_with_the_sample_recipes()
    {
        using var cookbook = new TestCookbook();

        Assert.True(await Seeder(cookbook).TrySeedAsync());

        var recipes = await cookbook.Recipes.GetAllAsync();
        Assert.Equal(6, recipes.Count);
        Assert.All(recipes, recipe => Assert.False(string.IsNullOrWhiteSpace(recipe.Title)));
        Assert.All(recipes, recipe => Assert.NotEmpty(recipe.Ingredients));
        Assert.All(recipes, recipe => Assert.NotEmpty(recipe.Steps));
        Assert.Equal(2, recipes.Count(recipe => recipe.IsFavorite));
        Assert.Contains(recipes, recipe => recipe.Title == "Soto Ayam Lamongan");
    }

    [Fact]
    public async Task Every_sample_lands_in_a_category_that_exists()
    {
        using var cookbook = new TestCookbook();
        var categories = await cookbook.Categories.GetAllAsync();

        await Seeder(cookbook).TrySeedAsync();
        var recipes = await cookbook.Recipes.GetAllAsync();

        Assert.All(recipes, recipe =>
        {
            var category = Assert.Single(categories, c => c.Id == recipe.CategoryId);
            Assert.False(string.IsNullOrWhiteSpace(category.Name));
        });
    }

    [Fact]
    public async Task The_samples_are_added_only_once()
    {
        using var cookbook = new TestCookbook();
        await Seeder(cookbook).TrySeedAsync();
        var afterFirstRun = cookbook.CountRows("Recipes");

        var addedAgain = await Seeder(cookbook).TrySeedAsync();

        Assert.False(addedAgain);
        Assert.Equal(afterFirstRun, cookbook.CountRows("Recipes"));
    }

    [Fact]
    public async Task A_cookbook_the_cook_already_filled_is_left_alone()
    {
        using var cookbook = new TestCookbook();
        var mine = new Recipe
        {
            Id = Guid.NewGuid(),
            Title = "My grandmother's curry",
            Description = "The one with the burnt shallots.",
            Servings = 4,
            PrepMinutes = 15,
            CookMinutes = 45,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await cookbook.Recipes.SaveAsync(mine);

        var added = await Seeder(cookbook).TrySeedAsync();

        Assert.False(added);
        Assert.Equal(1, cookbook.CountRows("Recipes"));
        Assert.True(cookbook.Settings.Current.SamplesAdded);
    }

    [Fact]
    public async Task An_abandoned_new_recipe_screen_does_not_block_the_samples()
    {
        using var cookbook = new TestCookbook();

        // What the editor leaves behind when a cook opens "New recipe" and walks away.
        var draft = new Recipe
        {
            Id = Guid.NewGuid(),
            Title = "New recipe",
            Servings = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        draft.Ingredients.Add(new Ingredient
        {
            Id = Guid.NewGuid(),
            RecipeId = draft.Id,
            SortOrder = 1,
            Quantity = "1",
            Unit = "cup",
            Name = "Ingredient 1"
        });
        await cookbook.Recipes.SaveAsync(draft);

        Assert.True(await Seeder(cookbook).TrySeedAsync());
        Assert.Equal(7, cookbook.CountRows("Recipes"));
    }
}
