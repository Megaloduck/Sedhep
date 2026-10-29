using System;
using System.Collections.Generic;
using System.Linq;
using Sedhep.Domain;
using Xunit;

namespace Sedhep.Tests;

public sealed class IngredientAlmanacTests
{
    private static Recipe RecipeWith(string title, params (string Quantity, string Unit, string Name)[] ingredients)
    {
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var order = 1;
        foreach (var (quantity, unit, name) in ingredients)
        {
            recipe.Ingredients.Add(new Ingredient
            {
                Id = Guid.NewGuid(),
                RecipeId = recipe.Id,
                SortOrder = order++,
                Quantity = quantity,
                Unit = unit,
                Name = name
            });
        }

        return recipe;
    }

    [Fact]
    public void The_same_ingredient_written_differently_becomes_one_card()
    {
        var cookbook = new List<Recipe>
        {
            RecipeWith("Soto Ayam", ("1", "kg", "chicken")),
            RecipeWith("Ayam Bakar", ("500", "g", "Chicken,")),
            RecipeWith("Rendang", ("2", "kg", " beef "))
        };

        var almanac = IngredientAlmanac.Build(cookbook);

        var chicken = Assert.Single(almanac, entry => entry.Name == "Chicken");
        Assert.Equal(2, chicken.Recipes.Count);
        Assert.Equal(2, chicken.TimesUsed);
        Assert.Equal("Rendang", Assert.Single(almanac, entry => entry.Name == "Beef").Recipes[0].Title);
    }

    [Fact]
    public void Plural_and_singular_names_share_a_card()
    {
        var cookbook = new List<Recipe>
        {
            RecipeWith("One", ("5", "", "shallots")),
            RecipeWith("Two", ("2", "", "shallot"))
        };

        var almanac = IngredientAlmanac.Build(cookbook);

        var entry = Assert.Single(almanac);
        Assert.Equal(2, entry.Recipes.Count);
    }

    [Fact]
    public void A_recipe_that_lists_the_same_thing_twice_still_counts_once()
    {
        var cookbook = new List<Recipe>
        {
            RecipeWith("Sauce", ("1", "tsp", "salt"), ("1", "pinch", "salt"))
        };

        var entry = Assert.Single(IngredientAlmanac.Build(cookbook));

        Assert.Single(entry.Recipes);
        Assert.Equal(2, entry.TimesUsed);
    }

    [Theory]
    [InlineData("chicken", IngredientFamily.Protein)]
    [InlineData("tempeh", IngredientFamily.Protein)]
    [InlineData("whole milk", IngredientFamily.Dairy)]
    [InlineData("coconut milk", IngredientFamily.Pantry)]
    [InlineData("basmati rice", IngredientFamily.Grain)]
    [InlineData("palm sugar", IngredientFamily.Sweet)]
    [InlineData("turmeric", IngredientFamily.Spice)]
    [InlineData("lemongrass", IngredientFamily.Herb)]
    [InlineData("tomatoes", IngredientFamily.Produce)]
    [InlineData("soy sauce", IngredientFamily.Pantry)]
    [InlineData("saffron threads", IngredientFamily.Spice)]
    public void Ingredients_are_filed_on_the_right_shelf(string name, IngredientFamily expected)
    {
        Assert.Equal(expected, IngredientAlmanac.Classify(name));
    }

    [Fact]
    public void A_name_nobody_recognises_goes_to_the_pantry_instead_of_being_guessed()
    {
        Assert.Equal(IngredientFamily.Pantry, IngredientAlmanac.Classify("grandmother's secret powder"));
        Assert.Equal(IngredientFamily.Pantry, IngredientAlmanac.Classify("   "));
    }

    [Fact]
    public void Cards_are_ordered_by_how_many_recipes_use_them()
    {
        var cookbook = new List<Recipe>
        {
            RecipeWith("One", ("1", "tsp", "salt"), ("1", "kg", "chicken")),
            RecipeWith("Two", ("1", "tsp", "salt"), ("3", "", "garlic")),
            RecipeWith("Three", ("1", "tsp", "salt"), ("3", "", "garlic"))
        };

        var almanac = IngredientAlmanac.Build(cookbook);

        Assert.Equal(new[] { "Salt", "Garlic", "Chicken" }, almanac.Select(entry => entry.Name));
        Assert.Equal("1 tsp", almanac[0].UsualAmount);
    }

    [Fact]
    public void Blank_ingredient_rows_are_skipped()
    {
        var cookbook = new List<Recipe>
        {
            RecipeWith("Nameless", ("2", "", "   "))
        };

        Assert.Empty(IngredientAlmanac.Build(cookbook));
    }

    [Fact]
    public void A_card_says_which_shelf_it_belongs_to_in_words()
    {
        var cookbook = new List<Recipe> { RecipeWith("Soto", ("1", "kg", "chicken")) };

        var entry = Assert.Single(IngredientAlmanac.Build(cookbook));

        Assert.Equal(IngredientFamily.Protein, entry.Family);
        Assert.Equal("Protein", entry.FamilyName);
    }
}
