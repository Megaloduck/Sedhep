using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Media;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

public enum RecipeSort
{
    RecentlyUpdated,
    NewestFirst,
    Title
}

/// <summary>
/// The cookbook home: search, category filters and the list of recipes. The same page backs the
/// Favorites tab, which only changes the filter and the wording.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IRecipeRepository _recipes;
    private readonly ICategoryRepository _categories;
    private readonly IRecipePhotoLoader _photoLoader;
    private readonly ICookbookNavigator _navigator;
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>Guards against an older search finishing after a newer one.</summary>
    private int _loadVersion;

    /// <summary>
    /// How long the field rests before the database is asked again. Typing a word is one search,
    /// not one per letter: the last keystroke cancels the pause that earlier ones started.
    /// </summary>
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(220);

    private CancellationTokenSource? _searchCts;

    private CancellationTokenSource? _coverCts;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isFavoritesActive;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private CategoryFilterViewModel? _selectedCategory;

    [ObservableProperty]
    private RecipeSort _sort = RecipeSort.RecentlyUpdated;

    [ObservableProperty]
    private string _headerTitle = "My cookbook";

    [ObservableProperty]
    private string _headerSubtitle = "Yours to keep, on this device.";

    [ObservableProperty]
    private string _countLabel = string.Empty;

    [ObservableProperty]
    private string _emptyTitle = "Your cookbook is empty";

    [ObservableProperty]
    private string _emptyMessage =
        "Write down a recipe and it stays here — no account, no internet, no subscription.";

    public ObservableCollection<RecipeCardViewModel> Recipes { get; } = new();

    public ObservableCollection<CategoryFilterViewModel> CategoryFilters { get; } = new();

    public bool IsSortRecent => Sort == RecipeSort.RecentlyUpdated;

    public bool IsSortNewest => Sort == RecipeSort.NewestFirst;

    public bool IsSortTitle => Sort == RecipeSort.Title;

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    public MainViewModel(
        IRecipeRepository recipes,
        ICategoryRepository categories,
        IRecipePhotoLoader photoLoader,
        ICookbookNavigator navigator,
        ILogger<MainViewModel> logger)
    {
        _recipes = recipes;
        _categories = categories;
        _photoLoader = photoLoader;
        _navigator = navigator;
        _logger = logger;
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(ShowWriteCta));

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        // Emptying the field is a decision, not typing: show the whole cookbook straight away.
        if (string.IsNullOrWhiteSpace(value))
        {
            _ = RefreshAsync();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = SearchAfterPauseAsync(cts.Token);
    }

    private async Task SearchAfterPauseAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // A fresh RefreshAsync bumps the load version, so a slower earlier search cannot win.
        await RefreshAsync();
    }

    partial void OnIsFavoritesActiveChanged(bool value)
    {
        HeaderTitle = value ? "Favorites" : "My cookbook";
        HeaderSubtitle = value
            ? "The recipes you come back to."
            : "Yours to keep, on this device.";
        OnPropertyChanged(nameof(ShowWriteCta));
    }

    partial void OnSortChanged(RecipeSort value)
    {
        OnPropertyChanged(nameof(IsSortRecent));
        OnPropertyChanged(nameof(IsSortNewest));
        OnPropertyChanged(nameof(IsSortTitle));
        _ = RefreshAsync();
    }

    /// <summary>Loads the filter chips and the recipes; used when the page is first shown or the
    /// category list may have changed.</summary>
    public async Task InitializeAsync()
    {
        await LoadCategoryFiltersAsync();
        await RefreshAsync();
    }

    /// <summary>Re-reads the cookbook, e.g. after returning from the editor.</summary>
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
                Recipes.Add(new RecipeCardViewModel(recipe));
            }

            IsEmpty = Recipes.Count == 0;
            CountLabel = Recipes.Count == 1 ? "1 recipe" : $"{Recipes.Count} recipes";
            UpdateEmptyState();
            _logger.LogInformation("Cookbook home shows {Count} recipes", Recipes.Count);

            _ = LoadCoversAsync(Recipes.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recipes");
            Status = "Could not read your recipes";
        }
    }

    private async Task LoadCoversAsync(IReadOnlyList<RecipeCardViewModel> cards)
    {
        _coverCts?.Cancel();
        _coverCts = new CancellationTokenSource();
        var token = _coverCts.Token;

        foreach (var card in cards)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (card.Recipe.CoverPhoto is { } photo)
            {
                try
                {
                    card.Cover = await _photoLoader.LoadAsync(photo, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void UpdateEmptyState()
    {
        // A search that found nothing is the most specific thing to say, so it wins.
        if (IsSearching)
        {
            EmptyTitle = $"Nothing matched “{SearchText.Trim()}”";
            EmptyMessage = "Try a different word, or search by an ingredient such as chicken.";
            OnPropertyChanged(nameof(ShowWriteCta));
            return;
        }

        if (IsFavoritesActive)
        {
            EmptyTitle = "No favorites yet";
            EmptyMessage = "Open a recipe and tap the star to keep it close.";
            OnPropertyChanged(nameof(ShowWriteCta));
            return;
        }

        EmptyTitle = "Your cookbook is empty";
        EmptyMessage = "Write down a recipe and it stays here — no account, no internet, no subscription.";
        OnPropertyChanged(nameof(ShowWriteCta));
    }

    /// <summary>An empty cookbook wants a first recipe; an empty favorites list does not.</summary>
    public bool ShowWriteCta => !IsSearching && !IsFavoritesActive;

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

        return Sort switch
        {
            RecipeSort.Title => recipes.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
            RecipeSort.NewestFirst => recipes.OrderByDescending(r => r.CreatedAt).ToList(),
            _ => recipes.OrderByDescending(r => r.UpdatedAt).ToList()
        };
    }

    [RelayCommand]
    private void NewRecipe() => _navigator.ShowEditor(existing: null);

    [RelayCommand]
    private void OpenRecipe(RecipeCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        _navigator.ShowRecipe(card.Recipe);
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
    private void SortRecent() => Sort = RecipeSort.RecentlyUpdated;

    [RelayCommand]
    private void SortNewest() => Sort = RecipeSort.NewestFirst;

    [RelayCommand]
    private void SortByTitle() => Sort = RecipeSort.Title;

    [RelayCommand]
    private void ManageCategories() => _navigator.ShowCategoryManager();
}
