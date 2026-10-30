// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Sedhep.Domain;

namespace Sedhep.Infrastructure;

/// <summary>
/// The small cookbook Sedhep ships with. A brand new cookbook is an empty grid with a create
/// button, which says nothing about what the app is for, so the first run starts from six
/// ordinary recipes: real amounts, real steps, two of them already marked as favourites. They are
/// written through the normal repository, so editing one or deleting it behaves exactly like a
/// recipe the cook wrote, and nothing here is protected once it has been copied in.
/// </summary>
public static class SampleCookbook
{
    /// <summary>
    /// Builds fresh copies of the sample recipes. Ids are new on every call so that two devices
    /// seeding their own cookbooks never collide in a later transfer.
    /// </summary>
    /// <param name="categories">The categories the cookbook already has; samples are filed by name.</param>
    /// <param name="sourceDeviceId">Recorded as where the sample came from, like any other recipe.</param>
    /// <param name="now">The moment the samples are added; each recipe is dated a little before it.</param>
    public static IReadOnlyList<Recipe> Create(
        IReadOnlyList<Category> categories,
        string sourceDeviceId,
        DateTimeOffset now)
    {
        var recipes = new List<Recipe>(Specs.Count);

        foreach (var spec in Specs)
        {
            var recipe = new Recipe
            {
                Id = Guid.NewGuid(),
                Title = spec.Title,
                Description = spec.Description,
                CategoryId = FindCategoryId(categories, spec.Category),
                CategoryName = spec.Category,
                Cuisine = spec.Cuisine,
                Servings = spec.Servings,
                PrepMinutes = spec.PrepMinutes,
                CookMinutes = spec.CookMinutes,
                IsFavorite = spec.IsFavorite,
                CreatedAt = now.AddDays(-spec.AddedDaysAgo),
                UpdatedAt = now.AddDays(-spec.AddedDaysAgo),
                SourceDeviceId = sourceDeviceId,
                Notes = spec.Notes,
                Tags = spec.Tags.ToList()
            };

            var order = 0;
            foreach (var (quantity, unit, name) in spec.Ingredients)
            {
                recipe.Ingredients.Add(new Ingredient
                {
                    Id = Guid.NewGuid(),
                    RecipeId = recipe.Id,
                    SortOrder = ++order,
                    Name = name,
                    Quantity = quantity,
                    Unit = unit
                });
            }

            order = 0;
            foreach (var (instruction, minutes) in spec.Steps)
            {
                recipe.Steps.Add(new RecipeStep
                {
                    Id = Guid.NewGuid(),
                    RecipeId = recipe.Id,
                    SortOrder = ++order,
                    Instruction = instruction,
                    Minutes = minutes
                });
            }

            recipes.Add(recipe);
        }

        return recipes;
    }

    /// <summary>
    /// A sample may name a category the cook has renamed or deleted. The recipe still has to land
    /// somewhere sensible: "Other" if it exists, otherwise whatever the cookbook starts with.
    /// </summary>
    private static Guid FindCategoryId(IReadOnlyList<Category> categories, string name)
    {
        var match = categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? categories.FirstOrDefault(c => string.Equals(c.Name, "Other", StringComparison.OrdinalIgnoreCase))
                    ?? categories.FirstOrDefault();

        return match?.Id ?? Guid.Empty;
    }

    private static readonly IReadOnlyList<Spec> Specs = new List<Spec>
    {
        new(
            Title: "Soto Ayam Lamongan",
            Category: "Soup",
            Description: "The Sunday soup: turmeric broth, shredded chicken and a spoonful of sambal on top.",
            Servings: 6, PrepMinutes: 25, CookMinutes: 40, IsFavorite: true, AddedDaysAgo: 9,
            Cuisine: "Indonesian",
            Notes: "Skim the foam early for a clear broth. The koya goes in at the table, never in the pot.",
            Tags: new[] { "sunday", "mom's" },
            Ingredients: new[]
            {
                ("1", "kg", "chicken"), ("5", "", "shallots"), ("3", "cloves", "garlic"),
                ("2", "stalks", "lemongrass"), ("1", "tsp", "turmeric"), ("2", "tsp", "salt"),
                ("2", "L", "water"), ("2", "", "lime leaves"), ("1", "handful", "bean sprouts")
            },
            Steps: new[]
            {
                ("Boil the chicken with the lime leaves until tender, then lift it out and shred the meat.", 35),
                ("Grind the shallots, garlic and turmeric, then fry the paste until fragrant.", 8),
                ("Add the paste and lemongrass to the broth and simmer for twenty minutes.", 20),
                ("Serve with the shredded chicken, bean sprouts and a spoon of sambal.", 0)
            }),

        new(
            Title: "Rendang Daging",
            Category: "Main Dish",
            Description: "Dry-braised beef in coconut milk and spice, cooked slowly until the sauce clings to the meat.",
            Servings: 8, PrepMinutes: 30, CookMinutes: 180, IsFavorite: true, AddedDaysAgo: 6,
            Cuisine: "Indonesian",
            Notes: "Stir every ten minutes near the end, or the bottom catches.",
            Tags: new[] { "festive", "slow" },
            Ingredients: new[]
            {
                ("1.5", "kg", "beef"), ("800", "ml", "coconut milk"), ("10", "", "shallots"),
                ("6", "cloves", "garlic"), ("8", "", "chili"), ("2", "stalks", "lemongrass"),
                ("2", "tsp", "salt"), ("1", "tbsp", "palm sugar"), ("3", "", "turmeric leaves")
            },
            Steps: new[]
            {
                ("Grind the shallots, garlic and chili into a paste.", 10),
                ("Brown the beef in a heavy pot, then add the paste and lemongrass.", 12),
                ("Pour in the coconut milk and simmer, uncovered, until it thickens.", 90),
                ("Keep going until the sauce darkens and coats the meat, then rest before serving.", 45)
            }),

        new(
            Title: "Nasi Goreng Kampung",
            Category: "Main Dish",
            Description: "Friday-night fried rice with egg, sweet soy and just enough chili.",
            Servings: 2, PrepMinutes: 10, CookMinutes: 12, IsFavorite: false, AddedDaysAgo: 3,
            Cuisine: "Indonesian",
            Notes: "Cold rice from yesterday fries better than fresh rice.",
            Tags: new[] { "quick", "weeknight" },
            Ingredients: new[]
            {
                ("2", "cups", "rice"), ("2", "", "egg"), ("3", "", "shallots"),
                ("2", "cloves", "garlic"), ("2", "tbsp", "soy sauce"), ("1", "", "chili"),
                ("1", "tbsp", "oil"), ("1", "", "spring onion")
            },
            Steps: new[]
            {
                ("Fry the shallots, garlic and chili in the oil until soft.", 4),
                ("Push them aside, scramble the eggs, then add the rice.", 4),
                ("Season with soy sauce and toss over high heat until the rice is hot through.", 4)
            }),

        new(
            Title: "Ayam Bakar Kecap",
            Category: "Main Dish",
            Description: "Grilled chicken glazed with sweet soy and a squeeze of lime.",
            Servings: 4, PrepMinutes: 20, CookMinutes: 35, IsFavorite: false, AddedDaysAgo: 12,
            Cuisine: "Indonesian",
            Notes: "Score the chicken so the glaze reaches the inside.",
            Tags: new[] { "grill", "sunday" },
            Ingredients: new[]
            {
                ("4", "", "chicken"), ("4", "tbsp", "soy sauce"), ("2", "tbsp", "palm sugar"),
                ("3", "cloves", "garlic"), ("1", "tsp", "salt"), ("1", "", "lime")
            },
            Steps: new[]
            {
                ("Marinate the chicken in garlic, soy sauce and palm sugar for half an hour.", 30),
                ("Grill over medium heat, brushing with the glaze as it cooks.", 20),
                ("Rest for five minutes, then squeeze the lime over the top.", 5)
            }),

        new(
            Title: "Sambal Terasi",
            Category: "Sauce",
            Description: "A small, sharp bowl of chili and shrimp paste for the middle of the table.",
            Servings: 4, PrepMinutes: 10, CookMinutes: 5, IsFavorite: false, AddedDaysAgo: 2,
            Cuisine: "Indonesian",
            Notes: "Make it in a stone mortar if you have one; it tastes different.",
            Tags: new[] { "spicy" },
            Ingredients: new[]
            {
                ("10", "", "chili"), ("1", "tsp", "shrimp paste"), ("2", "", "tomato"),
                ("4", "", "shallots"), ("2", "cloves", "garlic"), ("1", "", "lime"),
                ("1", "tsp", "salt")
            },
            Steps: new[]
            {
                ("Fry the chili, tomato, shallots and garlic until softened.", 5),
                ("Grind with the shrimp paste and salt, then loosen with lime juice.", 2)
            }),

        new(
            Title: "Es Teh Manis",
            Category: "Drink",
            Description: "Sweet iced tea, the house drink with every meal.",
            Servings: 2, PrepMinutes: 5, CookMinutes: 0, IsFavorite: false, AddedDaysAgo: 1,
            Cuisine: "Indonesian",
            Notes: "Brew it strong; the ice melts into it.",
            Tags: new[] { "quick" },
            Ingredients: new[]
            {
                ("2", "", "tea bags"), ("3", "tbsp", "sugar"), ("500", "ml", "water"),
                ("1", "", "lemon"), ("1", "cups", "ice")
            },
            Steps: new[]
            {
                ("Brew the tea with the sugar, then cool it completely.", 0),
                ("Pour over ice and finish with a slice of lemon.", 0)
            })
    };

    private sealed record Spec(
        string Title,
        string Category,
        string Description,
        int Servings,
        int PrepMinutes,
        int CookMinutes,
        bool IsFavorite,
        int AddedDaysAgo,
        string Cuisine,
        string Notes,
        IReadOnlyList<string> Tags,
        IReadOnlyList<(string Quantity, string Unit, string Name)> Ingredients,
        IReadOnlyList<(string Instruction, int Minutes)> Steps);
}
