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

/// <summary>
/// Writing or changing a recipe. Everything is optional except the name, and ingredients and steps
/// can be added, reordered and removed one at a time — a non-technical user should never have to
/// start over because of a wrong row in the middle.
/// </summary>
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
    private string? _notes;

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

    [ObservableProperty]
    private string _tagInput = string.Empty;

    [ObservableProperty]
    private bool _hasPhotos;

    [ObservableProperty]
    private bool _hasIngredients;

    [ObservableProperty]
    private bool _hasSteps;

    public ObservableCollection<IngredientRowViewModel> IngredientRows { get; } = new();
    public ObservableCollection<StepRowViewModel> StepRows { get; } = new();
    public ObservableCollection<RecipePhotoViewModel> Photos { get; } = new();
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = new();
    public ObservableCollection<string> Tags { get; } = new();

    public string Header => IsNew ? "New recipe" : "Edit recipe";

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
        Notes = _recipe.Notes;
        Servings = _recipe.Servings <= 0 ? 4 : _recipe.Servings;
        PrepMinutes = _recipe.PrepMinutes;
        CookMinutes = _recipe.CookMinutes;

        foreach (var ingredient in _recipe.Ingredients.OrderBy(i => i.SortOrder))
        {
            IngredientRows.Add(new IngredientRowViewModel(ingredient));
        }

        foreach (var step in _recipe.Steps.OrderBy(s => s.SortOrder))
        {
            StepRows.Add(new StepRowViewModel(step));
        }

        foreach (var tag in _recipe.Tags)
        {
            Tags.Add(tag);
        }

        foreach (var photo in _recipe.Photos.OrderByDescending(p => p.IsCover).ThenByDescending(p => p.CapturedAt))
        {
            Photos.Add(new RecipePhotoViewModel(photo));
        }

        Renumber();
        HasPhotos = Photos.Count > 0;
        HasIngredients = IngredientRows.Count > 0;
        HasSteps = StepRows.Count > 0;

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

    private void Renumber()
    {
        for (var i = 0; i < IngredientRows.Count; i++)
        {
            IngredientRows[i].Number = i + 1;
        }

        for (var i = 0; i < StepRows.Count; i++)
        {
            StepRows[i].Number = i + 1;
        }

        HasIngredients = IngredientRows.Count > 0;
        HasSteps = StepRows.Count > 0;
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

            HasPhotos = Photos.Count > 0;
            Status = "Photo added. Save the recipe to keep it.";
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

        HasPhotos = Photos.Count > 0;
        Status = "Photo removed. Save the recipe to keep the change.";
    }

    [RelayCommand]
    private void AddIngredient()
    {
        IngredientRows.Add(new IngredientRowViewModel(new Ingredient
        {
            Id = Guid.NewGuid(),
            RecipeId = _recipe.Id
        }));

        Renumber();
    }

    [RelayCommand]
    private void RemoveIngredient(IngredientRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        IngredientRows.Remove(row);
        Renumber();
    }

    [RelayCommand]
    private void MoveIngredientUp(IngredientRowViewModel? row) => Move(IngredientRows, row, -1);

    [RelayCommand]
    private void MoveIngredientDown(IngredientRowViewModel? row) => Move(IngredientRows, row, 1);

    [RelayCommand]
    private void AddStep()
    {
        StepRows.Add(new StepRowViewModel(new RecipeStep
        {
            Id = Guid.NewGuid(),
            RecipeId = _recipe.Id
        }));

        Renumber();
    }

    [RelayCommand]
    private void RemoveStep(StepRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        StepRows.Remove(row);
        Renumber();
    }

    [RelayCommand]
    private void MoveStepUp(StepRowViewModel? row) => Move(StepRows, row, -1);

    [RelayCommand]
    private void MoveStepDown(StepRowViewModel? row) => Move(StepRows, row, 1);

    private void Move<T>(ObservableCollection<T> rows, T? row, int delta)
    {
        if (row is null)
        {
            return;
        }

        var index = rows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= rows.Count)
        {
            return;
        }

        rows.Move(index, target);
        Renumber();
    }

    [RelayCommand]
    private void AddTag()
    {
        var tag = TagInput.Trim().TrimEnd(',');
        if (tag.Length == 0)
        {
            return;
        }

        if (Tags.Any(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)))
        {
            TagInput = string.Empty;
            return;
        }

        Tags.Add(tag);
        TagInput = string.Empty;
        OnPropertyChanged(nameof(HasTags));
    }

    [RelayCommand]
    private void RemoveTag(string? tag)
    {
        if (tag is null)
        {
            return;
        }

        Tags.Remove(tag);
        OnPropertyChanged(nameof(HasTags));
    }

    public bool HasTags => Tags.Count > 0;

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
        _recipe.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        _recipe.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        _recipe.Servings = Servings <= 0 ? 1 : Servings;
        _recipe.PrepMinutes = Math.Max(0, PrepMinutes);
        _recipe.CookMinutes = Math.Max(0, CookMinutes);
        _recipe.UpdatedAt = DateTimeOffset.UtcNow;
        _recipe.Tags = Tags.ToList();

        var ingredients = new List<Ingredient>();
        for (var i = 0; i < IngredientRows.Count; i++)
        {
            var ingredient = IngredientRows[i].Model;
            if (string.IsNullOrWhiteSpace(ingredient.Name)
                && string.IsNullOrWhiteSpace(ingredient.Quantity)
                && string.IsNullOrWhiteSpace(ingredient.Unit))
            {
                continue;
            }

            ingredient.RecipeId = _recipe.Id;
            ingredient.SortOrder = ingredients.Count + 1;
            ingredients.Add(ingredient);
        }

        var steps = new List<RecipeStep>();
        for (var i = 0; i < StepRows.Count; i++)
        {
            var step = StepRows[i].Model;
            if (string.IsNullOrWhiteSpace(step.Instruction))
            {
                continue;
            }

            step.RecipeId = _recipe.Id;
            step.SortOrder = steps.Count + 1;
            steps.Add(step);
        }

        _recipe.Ingredients = ingredients;
        _recipe.Steps = steps;
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
