using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Media;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

/// <summary>
/// A single recipe as the user reads it, with the actions the plan puts on this screen:
/// favorite, cook, edit and delete.
/// </summary>
public partial class RecipeViewModel : ViewModelBase
{
    private readonly IRecipeRepository _recipes;
    private readonly ICookbookNavigator _navigator;
    private readonly IRecipePhotoLoader _photoLoader;
    private readonly ILogger<RecipeViewModel> _logger;

    [ObservableProperty]
    private Recipe _recipe = null!;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    private bool _hasPhoto;

    [ObservableProperty]
    private string? _status;

    public ObservableCollection<RecipePhotoViewModel> Photos { get; } = new();

    public RecipeViewModel(
        IRecipeRepository recipes,
        ICookbookNavigator navigator,
        IRecipePhotoLoader photoLoader,
        ILogger<RecipeViewModel> logger,
        Recipe recipe)
    {
        _recipes = recipes;
        _navigator = navigator;
        _photoLoader = photoLoader;
        _logger = logger;
        Recipe = recipe;

        // Only the photos of the recipe being looked at are decoded.
        _ = LoadPhotosAsync();
    }

    public string FavoriteLabel => Recipe.IsFavorite ? "★ Favorite" : "☆ Favorite";

    public string CategoryLabel => string.IsNullOrWhiteSpace(Recipe.CategoryName)
        ? "No category"
        : Recipe.CategoryName!;

    public bool HasIngredients => Recipe.Ingredients.Count > 0;

    public bool HasSteps => Recipe.Steps.Count > 0;

    private async Task LoadPhotosAsync()
    {
        var ordered = Recipe.Photos
            .OrderByDescending(p => p.IsCover)
            .ThenByDescending(p => p.CapturedAt)
            .ToList();

        foreach (var photo in ordered)
        {
            var thumbnail = new RecipePhotoViewModel(photo);
            Photos.Add(thumbnail);
        }

        HasPhoto = Photos.Count > 0;

        foreach (var thumbnail in Photos)
        {
            thumbnail.Image = await _photoLoader.LoadAsync(thumbnail.Photo);
        }
    }

    [RelayCommand]
    private async Task ToggleFavorite()
    {
        try
        {
            Recipe.IsFavorite = !Recipe.IsFavorite;
            Recipe.UpdatedAt = DateTimeOffset.UtcNow;
            await _recipes.SaveAsync(Recipe);

            OnPropertyChanged(nameof(Recipe));
            OnPropertyChanged(nameof(FavoriteLabel));
            Status = Recipe.IsFavorite ? "Saved to favorites" : "Removed from favorites";
        }
        catch (Exception ex)
        {
            Recipe.IsFavorite = !Recipe.IsFavorite;
            _logger.LogError(ex, "Failed to change the favorite state");
            Status = "Could not save that change";
        }
    }

    [RelayCommand]
    private void Cook() => _navigator.ShowCookingMode(Recipe);

    [RelayCommand]
    private void Edit() => _navigator.ShowEditor(Recipe);

    [RelayCommand]
    private void Delete()
    {
        IsConfirmingDelete = true;
        Status = null;
    }

    [RelayCommand]
    private void KeepRecipe()
    {
        IsConfirmingDelete = false;
        Status = null;
    }

    [RelayCommand]
    private async Task DeleteConfirmed()
    {
        try
        {
            await _recipes.DeleteAsync(Recipe.Id);
            _logger.LogInformation("Deleted recipe {Title}", Recipe.Title);
            _navigator.ShowCookbook();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete recipe");
            IsConfirmingDelete = false;
            Status = "Could not delete this recipe";
        }
    }

    [RelayCommand]
    private void Back() => _navigator.GoBack();
}
