// Licensed under the MIT license. See LICENSE in the project root.

using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Sedhep.Domain;

namespace Sedhep.ViewModels;

/// <summary>
/// One recipe as it appears in a list. The card owns its cover image so a long cookbook never has
/// to decode every photo at once, and it turns the recipe into the single line of meta the list
/// shows.
/// </summary>
public sealed partial class RecipeCardViewModel : ViewModelBase
{
    public RecipeCardViewModel(Recipe recipe)
    {
        Recipe = recipe;
    }

    public Recipe Recipe { get; }

    [ObservableProperty]
    private Bitmap? _cover;

    public bool HasCover => Cover is not null;

    public string Title => Recipe.Title;

    public bool IsFavorite => Recipe.IsFavorite;

    /// <summary>First letter, shown in place of a photo when the recipe has none.</summary>
    public string Initial => string.IsNullOrWhiteSpace(Recipe.Title)
        ? "?"
        : Recipe.Title.Trim()[..1].ToUpperInvariant();

    public string MetaLine
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(3);

            if (!string.IsNullOrWhiteSpace(Recipe.CategoryName))
            {
                parts.Add(Recipe.CategoryName!);
            }

            if (Recipe.TotalMinutes > 0)
            {
                parts.Add(Recipe.TotalMinutes < 60
                    ? $"{Recipe.TotalMinutes} min"
                    : $"{Recipe.TotalMinutes / 60} h {Recipe.TotalMinutes % 60:00} min");
            }

            if (Recipe.Servings > 0)
            {
                parts.Add($"{Recipe.Servings} servings");
            }

            return parts.Count == 0 ? "No details yet" : string.Join(" · ", parts);
        }
    }

    public string IngredientCountLabel => Recipe.Ingredients.Count == 1
        ? "1 ingredient"
        : $"{Recipe.Ingredients.Count} ingredients";

    partial void OnCoverChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCover));
}
