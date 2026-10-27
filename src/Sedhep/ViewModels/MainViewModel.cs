using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

/// <summary>
/// The cookbook home: search, category and favorites filters, and the list of recipes to open.
/// Everything reads from the local database, so it works with no connectivity.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IRecipeRepository _recipes;
    private readonly ICategoryRepository _categories;
    private readonly ICookbookTransferService _transfer;
    private readonly ICookbookNavigator _navigator;
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>Guards against an older search finishing after a newer one.</summary>
    private int _loadVersion;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isFavoritesActive;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private CategoryFilterViewModel? _selectedCategory;

    public ObservableCollection<Recipe> Recipes { get; } = new();

    public ObservableCollection<CategoryFilterViewModel> CategoryFilters { get; } = new();

    public MainViewModel(
        IRecipeRepository recipes,
        ICategoryRepository categories,
        ICookbookTransferService transfer,
        ICookbookNavigator navigator,
        ILogger<MainViewModel> logger)
    {
        _recipes = recipes;
        _categories = categories;
        _transfer = transfer;
        _navigator = navigator;
        _logger = logger;
    }

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();

    /// <summary>Loads the filter chips and the recipes; used when the page is first shown or the
    /// category list may have changed.</summary>
    public async Task InitializeAsync()
    {
        await LoadCategoryFiltersAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var version = ++_loadVersion;

        try
        {
            var results = ApplyFilters(await LoadAsync());

            if (version != _loadVersion)
            {
                return;
            }

            Recipes.Clear();
            foreach (var recipe in results)
            {
                Recipes.Add(recipe);
            }

            IsEmpty = Recipes.Count == 0;
            Status = Describe(Recipes.Count);
            _logger.LogInformation("Cookbook home shows {Count} recipes", Recipes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recipes");
            Status = "Could not read your recipes";
        }
    }

    private async Task LoadCategoryFiltersAsync()
    {
        try
        {
            var categories = await _categories.GetAllAsync();
            var previous = SelectedCategory?.CategoryId;

            CategoryFilters.Clear();
            CategoryFilters.Add(new CategoryFilterViewModel(null, "All", SelectCategoryCommand));
            foreach (var category in categories)
            {
                CategoryFilters.Add(new CategoryFilterViewModel(category.Id, category.Name, SelectCategoryCommand));
            }

            // Keep the user's filter if that category still exists.
            SelectedCategory = CategoryFilters.FirstOrDefault(f => f.CategoryId == previous && previous is not null)
                               ?? CategoryFilters[0];
            SyncActiveFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load categories");
        }
    }

    private void SyncActiveFilter()
    {
        foreach (var filter in CategoryFilters)
        {
            filter.IsActive = filter == SelectedCategory;
        }
    }

    /// <summary>
    /// A single filter can be answered by the database directly; combinations are narrowed
    /// in memory afterwards.
    /// </summary>
    private async Task<IReadOnlyList<Recipe>> LoadAsync()
    {
        var categoryId = SelectedCategory?.CategoryId;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            return await _recipes.SearchAsync(SearchText.Trim());
        }

        if (categoryId is { } id && !IsFavoritesActive)
        {
            return await _recipes.GetByCategoryAsync(id);
        }

        if (IsFavoritesActive && categoryId is null)
        {
            return await _recipes.GetFavoritesAsync();
        }

        return await _recipes.GetAllAsync();
    }

    private IReadOnlyList<Recipe> ApplyFilters(IReadOnlyList<Recipe> recipes)
    {
        if (SelectedCategory?.CategoryId is { } categoryId)
        {
            recipes = recipes.Where(r => r.CategoryId == categoryId).ToList();
        }

        if (IsFavoritesActive)
        {
            recipes = recipes.Where(r => r.IsFavorite).ToList();
        }

        return recipes;
    }

    private string Describe(int count)
    {
        var noun = count == 1 ? "recipe" : "recipes";
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            parts.Add($"matching “{SearchText.Trim()}”");
        }

        if (SelectedCategory?.CategoryId is not null)
        {
            parts.Add($"in {SelectedCategory.Name}");
        }

        if (IsFavoritesActive)
        {
            parts.Add("in favorites");
        }

        return parts.Count == 0
            ? $"{count} {noun}"
            : $"{count} {noun} {string.Join(' ', parts)}";
    }

    [RelayCommand]
    private void NewRecipe() => _navigator.ShowEditor(existing: null);

    [RelayCommand]
    private void OpenRecipe(Recipe? recipe)
    {
        if (recipe is null)
        {
            return;
        }

        _navigator.ShowRecipe(recipe);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private async Task SelectCategory(CategoryFilterViewModel? filter)
    {
        SelectedCategory = filter ?? CategoryFilters.FirstOrDefault();
        SyncActiveFilter();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ToggleFavorites()
    {
        IsFavoritesActive = !IsFavoritesActive;
        await RefreshAsync();
    }

    [RelayCommand]
    private void ManageCategories() => _navigator.ShowCategoryManager();

    [RelayCommand]
    private async Task Export()
    {
        Status = "Preparing cookbook file…";

        try
        {
            using var stream = await _transfer.ExportCookbookAsync(
                "cookbook-export",
                Array.ConvertAll(Recipes.ToArray(), recipe => recipe.Id.ToString()));

            _logger.LogInformation(
                "Exported {Count} recipes as {Bytes:N0} bytes",
                Recipes.Count,
                stream.Length);

            Status = $"Cookbook file ready — {Recipes.Count} recipes, {stream.Length:N0} bytes";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed");
            Status = "Could not prepare the cookbook file";
        }
    }
}
