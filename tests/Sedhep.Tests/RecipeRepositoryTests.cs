using System;
using System.Linq;
using System.Threading.Tasks;
using Sedhep.Domain;
using Xunit;

namespace Sedhep.Tests;

public sealed class RecipeRepositoryTests
{
    private static Recipe NewRecipe(string title) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Description = "A family recipe.",
        Servings = 4,
        PrepMinutes = 10,
        CookMinutes = 20,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Notes = "Do not rush the onions.",
        Tags = { "weeknight", "quick" }
    };

    [Fact]
    public async Task Saving_a_recipe_keeps_its_ingredients_steps_tags_and_notes()
    {
        using var cookbook = new TestCookbook();
        var recipe = NewRecipe("Soto Ayam");
        recipe.Ingredients.Add(new Ingredient
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            SortOrder = 1,
            Quantity = "1",
            Unit = "kg",
            Name = "chicken"
        });
        recipe.Steps.Add(new RecipeStep
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Boil the chicken.",
            Minutes = 30
        });

        await cookbook.Recipes.SaveAsync(recipe);
        var stored = await cookbook.Recipes.GetByIdAsync(recipe.Id);

        Assert.NotNull(stored);
        Assert.Equal("Soto Ayam", stored!.Title);
        Assert.Equal("Do not rush the onions.", stored.Notes);
        Assert.Equal(new[] { "weeknight", "quick" }, stored.Tags);
        Assert.Single(stored.Ingredients);
        Assert.Equal("chicken", stored.Ingredients.Single().Name);
        Assert.Single(stored.Steps);
        Assert.Equal(30, stored.Steps.Single().Minutes);
    }

    [Fact]
    public async Task Saving_an_existing_recipe_bumps_its_revision()
    {
        using var cookbook = new TestCookbook();
        var recipe = NewRecipe("Rendang");
        await cookbook.Recipes.SaveAsync(recipe);
        Assert.Equal(1, recipe.Revision);

        recipe.Title = "Rendang Daging";
        await cookbook.Recipes.SaveAsync(recipe);

        Assert.Equal(2, recipe.Revision);
        var stored = await cookbook.Recipes.GetByIdAsync(recipe.Id);
        Assert.Equal("Rendang Daging", stored!.Title);
        Assert.Equal(2, stored.Revision);
    }

    [Fact]
    public async Task Deleting_a_recipe_leaves_a_tombstone_and_removes_its_children()
    {
        using var cookbook = new TestCookbook();
        var recipe = NewRecipe("Nasi Goreng");
        recipe.Ingredients.Add(new Ingredient
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            SortOrder = 1,
            Name = "rice"
        });
        await cookbook.Recipes.SaveAsync(recipe);

        await cookbook.Recipes.DeleteAsync(recipe.Id);

        Assert.Null(await cookbook.Recipes.GetByIdAsync(recipe.Id));
        Assert.Equal(1, cookbook.CountRows("DeletedRecipes"));
        Assert.Equal(0, cookbook.CountRows("Ingredients"));
    }

    [Fact]
    public async Task Searching_matches_tags_ingredients_and_notes()
    {
        using var cookbook = new TestCookbook();

        var soto = NewRecipe("Soto Ayam");
        soto.Tags = new[] { "sunday" };
        soto.Ingredients.Add(new Ingredient
        {
            Id = Guid.NewGuid(),
            RecipeId = soto.Id,
            SortOrder = 1,
            Name = "lemongrass"
        });

        var cake = NewRecipe("Kue Lapis");
        cake.Tags = new[] { "birthday" };
        cake.Notes = "Grandma's handwriting is on the tin.";

        await cookbook.Recipes.SaveAsync(soto);
        await cookbook.Recipes.SaveAsync(cake);

        Assert.Equal("Soto Ayam", (await cookbook.Recipes.SearchAsync("sunday")).Single().Title);
        Assert.Equal("Soto Ayam", (await cookbook.Recipes.SearchAsync("lemongrass")).Single().Title);
        Assert.Equal("Kue Lapis", (await cookbook.Recipes.SearchAsync("handwriting")).Single().Title);
        Assert.Empty(await cookbook.Recipes.SearchAsync("chocolate"));
    }

    [Fact]
    public async Task Deleting_a_category_keeps_the_recipes_it_held()
    {
        using var cookbook = new TestCookbook();
        var category = new Category { Id = Guid.NewGuid(), Name = "Indonesian" };
        await cookbook.Categories.SaveAsync(category);

        var recipe = NewRecipe("Ayam Bakar");
        recipe.CategoryId = category.Id;
        await cookbook.Recipes.SaveAsync(recipe);

        await cookbook.Categories.DeleteAsync(category.Id);

        var stored = await cookbook.Recipes.GetByIdAsync(recipe.Id);
        Assert.NotNull(stored);
        Assert.Equal(Guid.Empty, stored!.CategoryId);
        Assert.Null(stored.CategoryName);
    }

    [Fact]
    public async Task A_brand_new_cookbook_is_seeded_with_the_categories_from_the_plan()
    {
        using var cookbook = new TestCookbook();
        var categories = await cookbook.Categories.GetAllAsync();

        Assert.Equal(9, categories.Count);
        Assert.Contains(categories, c => c.Name == "Main Dish");
        Assert.Contains(categories, c => c.Name == "Dessert");
    }
}
