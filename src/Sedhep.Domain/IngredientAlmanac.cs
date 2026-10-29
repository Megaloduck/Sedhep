// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sedhep.Domain;

/// <summary>
/// The shelf an ingredient belongs to. The almanac groups ingredients this way so a card can be
/// filed and tinted without asking the user to invent a taxonomy.
/// </summary>
public enum IngredientFamily
{
    Produce,
    Protein,
    Spice,
    Herb,
    Grain,
    Dairy,
    Sweet,
    Pantry
}

/// <summary>
/// One ingredient as the almanac files it: the name as the cook writes it, the shelf it belongs to,
/// every recipe that calls for it, and the measure it most often appears with.
/// </summary>
public sealed record IngredientAlmanacEntry(
    string Name,
    IngredientFamily Family,
    IReadOnlyList<Recipe> Recipes,
    int TimesUsed,
    string? UsualAmount)
{
    public string FamilyName => IngredientAlmanac.Describe(Family);
}

/// <summary>
/// Builds the almanac from the cookbook itself. There is no ingredient table to maintain: every
/// recipe already carries structured ingredient rows, so the glossary is a view over them and can
/// never drift out of date.
/// </summary>
public static class IngredientAlmanac
{
    /// <summary>Whole phrases first: "coconut milk" is a pantry staple, not dairy.</summary>
    private static readonly (string Phrase, IngredientFamily Family)[] Phrases =
    {
        ("coconut milk", IngredientFamily.Pantry),
        ("soy sauce", IngredientFamily.Pantry),
        ("fish sauce", IngredientFamily.Pantry),
        ("oyster sauce", IngredientFamily.Pantry),
        ("sesame oil", IngredientFamily.Pantry),
        ("olive oil", IngredientFamily.Pantry),
        ("chicken stock", IngredientFamily.Pantry),
        ("vegetable stock", IngredientFamily.Pantry),
        ("bell pepper", IngredientFamily.Produce),
        ("spring onion", IngredientFamily.Produce),
        ("palm sugar", IngredientFamily.Sweet),
        ("brown sugar", IngredientFamily.Sweet),
        ("bay leaf", IngredientFamily.Herb),
        ("curry powder", IngredientFamily.Spice),
        ("star anise", IngredientFamily.Spice),
        ("bean sprout", IngredientFamily.Produce)
    };

    private static readonly Dictionary<IngredientFamily, string[]> Keywords = new()
    {
        [IngredientFamily.Protein] = new[]
        {
            "chicken", "ayam", "beef", "daging", "pork", "lamb", "mutton", "meat", "duck",
            "fish", "ikan", "salmon", "tuna", "sardine", "shrimp", "prawn", "udang", "squid",
            "crab", "egg", "telur", "tofu", "tahu", "tempeh", "tempe", "bacon", "sausage",
            "ham", "anchovy", "teri"
        },
        [IngredientFamily.Dairy] = new[]
        {
            "milk", "susu", "butter", "mentega", "cheese", "keju", "cream", "yogurt", "yoghurt",
            "curd"
        },
        [IngredientFamily.Grain] = new[]
        {
            "rice", "beras", "nasi", "flour", "tepung", "bread", "pasta", "noodle", "mie",
            "oat", "quinoa", "couscous", "vermicelli", "semolina", "barley", "cracker"
        },
        [IngredientFamily.Sweet] = new[]
        {
            "sugar", "gula", "honey", "madu", "syrup", "chocolate", "cokelat", "vanilla",
            "molasses", "jam", "sweetener"
        },
        [IngredientFamily.Spice] = new[]
        {
            "salt", "pepper", "peppercorn", "chili", "chilli", "cabe", "cayenne", "turmeric",
            "kunyit", "cumin", "jintan", "coriander", "ketumbar", "cinnamon", "kayu manis",
            "clove", "cengkeh", "cardamom", "nutmeg", "paprika", "curry", "kari", "saffron",
            "ginger", "jahe", "galangal", "lengkuas", "anise", "spice"
        },
        [IngredientFamily.Herb] = new[]
        {
            "basil", "kemangi", "mint", "parsley", "cilantro", "thyme", "rosemary", "oregano",
            "dill", "lemongrass", "sereh", "pandan", "chive", "celery", "salam", "sage",
            "tarragon", "leaf"
        },
        [IngredientFamily.Produce] = new[]
        {
            "onion", "bawang", "shallot", "garlic", "tomato", "potato", "carrot", "cabbage",
            "spinach", "cucumber", "eggplant", "terong", "pumpkin", "broccoli", "mushroom",
            "lemon", "lime", "bean", "pea", "corn", "avocado", "banana", "apple", "mango",
            "orange", "papaya", "pineapple", "zucchini", "lettuce", "radish", "beet", "scallion",
            "leek", "sprout", "kale", "cauliflower", "choy", "taro", "cassava", "singkong"
        },
        [IngredientFamily.Pantry] = new[]
        {
            "water", "air", "oil", "minyak", "vinegar", "cuka", "sauce", "kecap", "paste",
            "stock", "broth", "wine", "yeast", "baking", "soda", "starch", "gelatin"
        }
    };

    /// <summary>
    /// Files every ingredient in the cookbook under one card. Recipes are counted once per
    /// ingredient, even if a recipe lists the same thing twice.
    /// </summary>
    public static IReadOnlyList<IngredientAlmanacEntry> Build(IEnumerable<Recipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);

        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);

        foreach (var recipe in recipes)
        {
            foreach (var ingredient in recipe.Ingredients)
            {
                var key = GroupKey(ingredient.Name);
                if (key.Length == 0)
                {
                    continue;
                }

                if (!groups.TryGetValue(key, out var group))
                {
                    groups[key] = group = new Group(key);
                }

                group.TimesUsed++;

                if (!string.IsNullOrWhiteSpace(ingredient.Name))
                {
                    group.Spellings[ingredient.Name.Trim()] = group.Spellings.GetValueOrDefault(ingredient.Name.Trim()) + 1;
                }

                var amount = FormatAmount(ingredient.Quantity, ingredient.Unit);
                if (amount is not null)
                {
                    group.Amounts[amount] = group.Amounts.GetValueOrDefault(amount) + 1;
                }

                group.Recipes.TryAdd(recipe.Id, recipe);
            }
        }

        return groups.Values
            .Select(group => new IngredientAlmanacEntry(
                group.DisplayName(),
                Classify(group.Key),
                group.Recipes.Values
                    .OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                group.TimesUsed,
                group.MostCommonAmount()))
            .OrderByDescending(entry => entry.Recipes.Count)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The shelf an ingredient belongs to. Unfamiliar names fall back to the pantry rather than
    /// being guessed at.
    /// </summary>
    public static IngredientFamily Classify(string ingredientName)
    {
        var normalized = Normalize(ingredientName);
        if (normalized.Length == 0)
        {
            return IngredientFamily.Pantry;
        }

        foreach (var (phrase, family) in Phrases)
        {
            if (normalized.Contains(phrase, StringComparison.Ordinal))
            {
                return family;
            }
        }

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Singular)
            .ToList();

        foreach (var family in Keywords.Keys)
        {
            foreach (var token in tokens)
            {
                if (Keywords[family].Contains(token, StringComparer.Ordinal))
                {
                    return family;
                }
            }
        }

        return IngredientFamily.Pantry;
    }

    /// <summary>The one line a card shows under the name.</summary>
    public static string Describe(IngredientFamily family) => family switch
    {
        IngredientFamily.Produce => "Fruit & vegetable",
        IngredientFamily.Protein => "Protein",
        IngredientFamily.Spice => "Spice",
        IngredientFamily.Herb => "Herb & leaf",
        IngredientFamily.Grain => "Grain & starch",
        IngredientFamily.Dairy => "Dairy",
        IngredientFamily.Sweet => "Sweetener",
        _ => "Pantry"
    };

    /// <summary>
    /// The grouping key: the same tidy-up as <see cref="Normalize"/>, then every word singular, so
    /// "shallots" and "shallot" are filed on one card.
    /// </summary>
    private static string GroupKey(string name) => string.Join(' ',
        Normalize(name)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Singular));

    /// <summary>Lowercase, punctuation-free and space-collapsed, so "Chicken," and "chicken" meet.</summary>
    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(name.Length);
        var lastWasSpace = true;

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// "shallots" and "shallot" are the same shelf; this is deliberately simple, because an
    /// ingredient name is prose, not a lexicon.
    /// </summary>
    private static string Singular(string token)
    {
        if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal))
        {
            return token[..^3] + "y";
        }

        if (token.Length > 4 && token.EndsWith("oes", StringComparison.Ordinal))
        {
            return token[..^2];
        }

        if (token.Length > 3 && token.EndsWith('s') &&
            !token.EndsWith("ss", StringComparison.Ordinal) &&
            !token.EndsWith("us", StringComparison.Ordinal))
        {
            return token[..^1];
        }

        return token;
    }

    private static string? FormatAmount(string? quantity, string? unit)
    {
        var q = quantity?.Trim();
        var u = unit?.Trim();

        if (string.IsNullOrEmpty(q) && string.IsNullOrEmpty(u))
        {
            return null;
        }

        return string.Join(' ', new[] { q, u }.Where(part => !string.IsNullOrEmpty(part)));
    }

    private sealed class Group
    {
        public Group(string key) => Key = key;

        public string Key { get; }

        public int TimesUsed { get; set; }

        public Dictionary<string, int> Spellings { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> Amounts { get; } = new(StringComparer.Ordinal);

        public Dictionary<Guid, Recipe> Recipes { get; } = new();

        /// <summary>The spelling the cook used most, tidied into a card title.</summary>
        public string DisplayName()
        {
            var best = Spellings
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key.Length)
                .Select(pair => pair.Key)
                .FirstOrDefault() ?? Key;

            return char.ToUpperInvariant(best[0]) + best[1..];
        }

        public string? MostCommonAmount() => Amounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Length)
            .Select(pair => pair.Key)
            .FirstOrDefault();
    }
}
