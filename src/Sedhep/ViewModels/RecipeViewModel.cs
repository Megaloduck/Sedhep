using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Media;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

/// <summary>
/// A single recipe as the user reads and cooks from it: favourite, cook, edit, share and delete.
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
    private bool _hasPhotos;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private Bitmap? _coverImage;

    public bool HasCover => CoverImage is not null;

    partial void OnCoverImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCover));

    public string Initial => string.IsNullOrWhiteSpace(Recipe.Title)
        ? "?"
        : Recipe.Title.Trim()[..1].ToUpperInvariant();

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
        IsFavorite = recipe.IsFavorite;

        // Only the photos of the recipe being looked at are decoded.
        _ = LoadPhotosAsync();
    }

    public string FavoriteLabel => IsFavorite ? "Saved" : "Save";

    public string CategoryLabel => string.IsNullOrWhiteSpace(Recipe.CategoryName)
        ? "No category"
        : Recipe.CategoryName!;

    public bool HasIngredients => Recipe.Ingredients.Count > 0;

    public bool HasSteps => Recipe.Steps.Count > 0;

    public bool HasNotes => !string.IsNullOrWhiteSpace(Recipe.Notes);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Recipe.Description);

    public bool HasTags => Recipe.Tags.Count > 0;

    public bool HasTime => Recipe.TotalMinutes > 0;

    public string TimeLabel => Recipe.TotalMinutes < 60
        ? $"{Recipe.TotalMinutes} min"
        : $"{Recipe.TotalMinutes / 60} h {Recipe.TotalMinutes % 60:00} min";

    public string ServingsLabel => Recipe.Servings == 1 ? "1 serving" : $"{Recipe.Servings} servings";

    public string StepCountLabel => Recipe.Steps.Count == 1 ? "1 step" : $"{Recipe.Steps.Count} steps";

    private async Task LoadPhotosAsync()
    {
        var ordered = Recipe.Photos
            .OrderByDescending(p => p.IsCover)
            .ThenByDescending(p => p.CapturedAt)
            .ToList();

        foreach (var photo in ordered)
        {
            Photos.Add(new RecipePhotoViewModel(photo));
        }

        HasPhotos = Photos.Count > 0;
        OnPropertyChanged(nameof(Photos));

        foreach (var thumbnail in Photos)
        {
            thumbnail.Image = await _photoLoader.LoadAsync(thumbnail.Photo);
        }

        CoverImage = Photos.FirstOrDefault(p => p.Photo.IsCover)?.Image ?? Photos.FirstOrDefault()?.Image;
        OnPropertyChanged(nameof(HasCover));
    }

    [RelayCommand]
    private async Task ToggleFavorite()
    {
        try
        {
            IsFavorite = !IsFavorite;
            Recipe.IsFavorite = IsFavorite;
            Recipe.UpdatedAt = DateTimeOffset.UtcNow;
            await _recipes.SaveAsync(Recipe);

            OnPropertyChanged(nameof(FavoriteLabel));
            Status = IsFavorite ? "Kept in favorites" : "Removed from favorites";
        }
        catch (Exception ex)
        {
            IsFavorite = !IsFavorite;
            Recipe.IsFavorite = IsFavorite;
            _logger.LogError(ex, "Failed to change the favorite state");
            Status = "Could not save that change";
        }
    }

    [RelayCommand]
    private void Cook() => _navigator.ShowCookingMode(Recipe);

    [RelayCommand]
    private void Edit() => _navigator.ShowEditor(Recipe);

    [RelayCommand]
    private void Share() => _navigator.ShowShare(Recipe.Id);

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
