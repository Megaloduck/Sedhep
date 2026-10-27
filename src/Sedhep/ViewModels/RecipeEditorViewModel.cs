using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Media;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

public partial class RecipeEditorViewModel : ViewModelBase
{
    private readonly IRecipeRepository _recipes;
    private readonly ICategoryRepository _categories;
    private readonly IRecipePhotoStorage _photoStorage;
    private readonly IRecipePhotoLoader _photoLoader;
    private readonly ICookbookNavigator _navigator;
    private readonly ILogger<RecipeEditorViewModel> _logger;
    private readonly Recipe _recipe;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string _cuisine = string.Empty;

    [ObservableProperty]
    private int _servings = 4;

    [ObservableProperty]
    private int _prepMinutes;

    [ObservableProperty]
    private int _cookMinutes;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private CategoryOption? _selectedCategory;

    [ObservableProperty]
    private bool _isNew;

    public ObservableCollection<Ingredient> Ingredients { get; } = new();
    public ObservableCollection<RecipeStep> Steps { get; } = new();
    public ObservableCollection<RecipePhotoViewModel> Photos { get; } = new();
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = new();

    public string Header => IsNew ? "New recipe" : "Edit recipe";

    public bool HasPhotos => Photos.Count > 0;

    public RecipeEditorViewModel(
        IRecipeRepository recipes,
        ICategoryRepository categories,
        IRecipePhotoStorage photoStorage,
        IRecipePhotoLoader photoLoader,
        ICookbookNavigator navigator,
        ILogger<RecipeEditorViewModel> logger,
        Recipe? existing = null)
    {
        _recipes = recipes;
        _categories = categories;
        _photoStorage = photoStorage;
        _photoLoader = photoLoader;
        _navigator = navigator;
        _logger = logger;
        IsNew = existing is null;
        _recipe = existing ?? new Recipe
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Title = _recipe.Title;
        Description = _recipe.Description;
        Cuisine = _recipe.Cuisine;
        Servings = _recipe.Servings;
        PrepMinutes = _recipe.PrepMinutes;
        CookMinutes = _recipe.CookMinutes;

        foreach (var ingredient in _recipe.Ingredients.OrderBy(i => i.SortOrder))
        {
            Ingredients.Add(ingredient);
        }

        foreach (var step in _recipe.Steps.OrderBy(s => s.SortOrder))
        {
            Steps.Add(step);
        }

        foreach (var photo in _recipe.Photos.OrderByDescending(p => p.IsCover).ThenByDescending(p => p.CapturedAt))
        {
            Photos.Add(new RecipePhotoViewModel(photo));
        }

        _ = LoadCategoriesAsync();
        _ = LoadPhotoImagesAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var all = await _categories.GetAllAsync();

            CategoryOptions.Clear();
            CategoryOptions.Add(new CategoryOption(Guid.Empty, "No category"));
            foreach (var category in all)
            {
                CategoryOptions.Add(new CategoryOption(category.Id, category.Name));
            }

            SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Id == _recipe.CategoryId)
                               ?? CategoryOptions[0];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load categories for the editor");
        }
    }

    private async Task LoadPhotoImagesAsync()
    {
        foreach (var thumbnail in Photos)
        {
            thumbnail.Image = await _photoLoader.LoadAsync(thumbnail.Photo);
        }
    }

    /// <summary>Stores a chosen photo next to the cookbook and attaches it to the recipe.</summary>
    public async Task AddPhotoAsync(Stream source, string fileName)
    {
        try
        {
            var storageId = Guid.NewGuid().ToString("N");
            var storedPath = await _photoStorage.StoreAsync(_recipe.Id.ToString(), storageId, source);
            var storedFile = new FileInfo(storedPath);

            var photo = new RecipePhoto
            {
                Id = Guid.NewGuid(),
                RecipeId = _recipe.Id,
                StorageId = storageId,
                FileName = string.IsNullOrWhiteSpace(fileName) ? "photo.jpg" : fileName,
                SizeBytes = storedFile.Exists ? storedFile.Length : 0,
                CapturedAt = DateTimeOffset.UtcNow,
                IsCover = Photos.Count == 0
            };

            var thumbnail = new RecipePhotoViewModel(photo);
            Photos.Add(thumbnail);
            thumbnail.Image = await _photoLoader.LoadAsync(photo);

            OnPropertyChanged(nameof(HasPhotos));
            Status = "Photo added. Tap “Save recipe” to keep it.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store the chosen photo");
            Status = "Could not save that photo";
        }
    }

    [RelayCommand]
    private void RemovePhoto(RecipePhotoViewModel? thumbnail)
    {
        if (thumbnail is null || !Photos.Remove(thumbnail))
        {
            return;
        }

        // Whatever is left at the front becomes the cover.
        for (var i = 0; i < Photos.Count; i++)
        {
            Photos[i].Photo.IsCover = i == 0;
            Photos[i].Refresh();
        }

        OnPropertyChanged(nameof(HasPhotos));
        Status = "Photo removed. Tap “Save recipe” to keep the change.";
    }

    [RelayCommand]
    private void AddIngredient()
    {
        Ingredients.Add(new Ingredient
        {
            Id = Guid.NewGuid(),
            RecipeId = _recipe.Id,
            SortOrder = Ingredients.Count + 1
        });
    }

    [RelayCommand]
    private void RemoveIngredient()
    {
        if (Ingredients.Count > 0)
        {
            Ingredients.RemoveAt(Ingredients.Count - 1);
        }
    }

    [RelayCommand]
    private void AddStep()
    {
        Steps.Add(new RecipeStep
        {
            Id = Guid.NewGuid(),
            RecipeId = _recipe.Id,
            SortOrder = Steps.Count + 1
        });
    }

    [RelayCommand]
    private void RemoveStep()
    {
        if (Steps.Count > 0)
        {
            Steps.RemoveAt(Steps.Count - 1);
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        // The only field the product insists on is a recipe name.
        if (string.IsNullOrWhiteSpace(Title))
        {
            Status = "Give the recipe a name before saving";
            return;
        }

        _recipe.Title = Title.Trim();
        _recipe.Description = Description;
        _recipe.Cuisine = Cuisine;
        _recipe.Servings = Servings;
        _recipe.PrepMinutes = PrepMinutes;
        _recipe.CookMinutes = CookMinutes;
        _recipe.UpdatedAt = DateTimeOffset.UtcNow;
        _recipe.Ingredients = Ingredients.ToList();
        _recipe.Steps = Steps.ToList();
        _recipe.Photos = Photos.Select(p => p.Photo).ToList();

        // Null only before the picker has loaded; then the recipe keeps the category it had.
        _recipe.CategoryId = SelectedCategory?.Id ?? _recipe.CategoryId;

        try
        {
            await _recipes.SaveAsync(_recipe);
            _logger.LogInformation("Saved recipe {Title}", _recipe.Title);
            _navigator.ShowCookbook();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save recipe");
            Status = "Could not save this recipe";
        }
    }

    [RelayCommand]
    private void Cancel() => _navigator.GoBack();
}
