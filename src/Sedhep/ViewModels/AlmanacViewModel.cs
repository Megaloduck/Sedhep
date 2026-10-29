using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Media;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

/// <summary>
/// The ingredients almanac: every ingredient the cookbook mentions, filed on a card with its shelf,
/// the measure it usually appears with, and the recipes that call for it. It is built from the
/// recipes themselves, so it is always current and needs no maintenance of its own.
/// </summary>
public partial class AlmanacViewModel : ViewModelBase
{
    private readonly IRecipeRepository _recipes;
    private readonly ICookbookNavigator _navigator;
    private readonly ILogger<AlmanacViewModel> _logger;

    private IReadOnlyList<IngredientAlmanacEntry> _all = Array.Empty<IngredientAlmanacEntry>();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _countLabel = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private AlmanacCardViewModel? _selected;

    [ObservableProperty]
    private AlmanacFamilyFilterViewModel? _selectedFamily;

    private bool _failed;

    public ObservableCollection<AlmanacCardViewModel> Cards { get; } = new();

    public ObservableCollection<AlmanacFamilyFilterViewModel> Families { get; } = new();

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    public bool HasSelection => Selected is not null;

    public bool HasAnyIngredients => _all.Count > 0;

    public string EmptyTitle => _failed
        ? "The almanac could not be read"
        : HasAnyIngredients
            ? IsSearching ? $"Nothing matched “{SearchText.Trim()}”" : "No ingredients on this shelf"
            : "The almanac is waiting";

    public string EmptyMessage => _failed
        ? "Your recipes are safe. Try opening this page again."
        : HasAnyIngredients
            ? "Try another word, or choose a different shelf above."
            : "Every ingredient you write into a recipe is filed here, with the recipes that use it.";

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        ApplyFilters();
    }

    partial void OnSelectedChanged(AlmanacCardViewModel? value) => OnPropertyChanged(nameof(HasSelection));

    public AlmanacViewModel(
        IRecipeRepository recipes,
        ICookbookNavigator navigator,
        ILogger<AlmanacViewModel> logger)
    {
        _recipes = recipes;
        _navigator = navigator;
        _logger = logger;
    }

    public async Task InitializeAsync() => await RefreshAsync();

    /// <summary>Re-reads the cookbook; the almanac is a view over recipes, so it is rebuilt whole.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var recipes = await _recipes.GetAllAsync();
            _all = IngredientAlmanac.Build(recipes);
            _failed = false;

            BuildFamilies();
            ApplyFilters();
            _logger.LogInformation("The almanac files {Count} ingredients", _all.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build the ingredient almanac");
            _failed = true;
            IsEmpty = true;
            OnPropertyChanged(nameof(EmptyTitle));
            OnPropertyChanged(nameof(EmptyMessage));
        }
    }

    private void BuildFamilies()
    {
        var previous = SelectedFamily?.Family;
        var present = _all.Select(entry => entry.Family).Distinct().OrderBy(family => family).ToList();

        Families.Clear();
        Families.Add(new AlmanacFamilyFilterViewModel(null, "All", SelectFamilyCommand));
        foreach (var family in present)
        {
            Families.Add(new AlmanacFamilyFilterViewModel(family, IngredientAlmanac.Describe(family), SelectFamilyCommand));
        }

        SelectedFamily = Families.FirstOrDefault(filter => filter.Family == previous) ?? Families[0];
        SyncActiveFamily();
    }

    private void ApplyFilters()
    {
        var query = IngredientAlmanac.Normalize(SearchText);
        var family = SelectedFamily?.Family;

        var matches = _all
            .Where(entry => family is null || entry.Family == family)
            .Where(entry => query.Length == 0 ||
                            IngredientAlmanac.Normalize(entry.Name).Contains(query, StringComparison.Ordinal) ||
                            IngredientAlmanac.Normalize(entry.FamilyName).Contains(query, StringComparison.Ordinal))
            .ToList();

        Cards.Clear();
        foreach (var entry in matches)
        {
            Cards.Add(new AlmanacCardViewModel(entry, OpenRecipeCommand));
        }

        IsEmpty = Cards.Count == 0;
        OnPropertyChanged(nameof(HasAnyIngredients));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
        CountLabel = Cards.Count == 1 ? "1 ingredient" : $"{Cards.Count} ingredients";
    }

    private void SyncActiveFamily()
    {
        foreach (var filter in Families)
        {
            filter.IsActive = filter == SelectedFamily;
        }
    }

    [RelayCommand]
    private void SelectFamily(AlmanacFamilyFilterViewModel? filter)
    {
        SelectedFamily = filter ?? Families.FirstOrDefault();
        SyncActiveFamily();
        ApplyFilters();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ShowCard(AlmanacCardViewModel? card) => Selected = card;

    [RelayCommand]
    private void CloseCard() => Selected = null;

    [RelayCommand]
    private void OpenRecipe(Recipe? recipe)
    {
        if (recipe is null)
        {
            return;
        }

        Selected = null;
        _navigator.ShowRecipe(recipe);
    }

    [RelayCommand]
    private void WriteRecipe() => _navigator.ShowEditor(existing: null);
}

/// <summary>One ingredient card: name, shelf, measure and the recipes that use it.</summary>
public sealed class AlmanacCardViewModel : ViewModelBase
{
    public AlmanacCardViewModel(IngredientAlmanacEntry entry, ICommand openRecipeCommand)
    {
        Entry = entry;

        var tone = CoverPalette.ForFamily(entry.Family);
        Wash = tone.Background;
        Ink = tone.Ink;

        Uses = entry.Recipes
            .Select(recipe => new AlmanacUseViewModel(recipe, openRecipeCommand))
            .ToList();
    }

    public IngredientAlmanacEntry Entry { get; }

    public string Name => Entry.Name;

    public string FamilyName => Entry.FamilyName;

    public IBrush Wash { get; }

    public IBrush Ink { get; }

    public IReadOnlyList<AlmanacUseViewModel> Uses { get; }

    public string Initial => Name.Length == 0 ? "?" : Name[..1].ToUpperInvariant();

    public string RecipeCountLabel => Entry.Recipes.Count == 1
        ? "In 1 recipe"
        : $"In {Entry.Recipes.Count} recipes";

    /// <summary>The short form, for the stat block where the label already says what it counts.</summary>
    public string RecipeCountShort => Entry.Recipes.Count == 1
        ? "1 recipe"
        : $"{Entry.Recipes.Count} recipes";

    public string UsesLabel => Entry.TimesUsed == 1 ? "used once" : $"used {Entry.TimesUsed} times";

    public string UsualAmountLabel => string.IsNullOrEmpty(Entry.UsualAmount)
        ? "No measure written"
        : $"Usually {Entry.UsualAmount}";
}

/// <summary>One recipe on an ingredient's card.</summary>
public sealed class AlmanacUseViewModel
{
    public AlmanacUseViewModel(Recipe recipe, ICommand openRecipeCommand)
    {
        Recipe = recipe;
        OpenCommand = new RelayCommand(() => openRecipeCommand.Execute(recipe));
    }

    public Recipe Recipe { get; }

    public ICommand OpenCommand { get; }

    public string Title => Recipe.Title;

    public string MetaLine
    {
        get
        {
            var parts = new List<string>(2);

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

            return parts.Count == 0 ? "No details yet" : string.Join(" · ", parts);
        }
    }
}

/// <summary>One shelf in the almanac filter row. A null family is "All".</summary>
public sealed partial class AlmanacFamilyFilterViewModel : ViewModelBase
{
    public AlmanacFamilyFilterViewModel(IngredientFamily? family, string name, ICommand selectCommand)
    {
        Family = family;
        Name = name;
        SelectCommand = selectCommand;
    }

    public IngredientFamily? Family { get; }

    public string Name { get; }

    public ICommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isActive;
}
