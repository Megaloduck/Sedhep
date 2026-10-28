using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sedhep.Domain;
using Sedhep.Navigation;

namespace Sedhep.ViewModels;

/// <summary>
/// Holds the page that is on screen, the three top-level tabs, and the history behind them so a
/// phone user can always walk back to the cookbook. Pages are created through factories to keep
/// this class free of container lookups.
/// </summary>
public partial class ShellViewModel : ViewModelBase, ICookbookNavigator
{
    /// <summary>
    /// Past this width the shell stops acting like a phone: the tabs move to a rail down the left
    /// and the content sits in one readable column.
    /// </summary>
    public const double WideBreakpoint = 760;

    private readonly Func<MainViewModel> _cookbookFactory;
    private readonly Func<Recipe, RecipeViewModel> _recipeFactory;
    private readonly Func<Recipe?, RecipeEditorViewModel> _editorFactory;
    private readonly Func<Recipe, CookingModeViewModel> _cookingModeFactory;
    private readonly Func<CategoryManagerViewModel> _categoryManagerFactory;
    private readonly Func<Guid?, ShareViewModel> _shareFactory;
    private readonly Func<SettingsViewModel> _settingsFactory;
    private readonly INearbyShareService _nearby;
    private readonly Stack<ViewModelBase> _history = new();

    private MainViewModel? _cookbook;

    [ObservableProperty]
    private ViewModelBase? _current;

    [ObservableProperty]
    private IncomingShareOffer? _incomingOffer;

    [ObservableProperty]
    private string? _offerStatus;

    [ObservableProperty]
    private bool _isWideLayout;

    public ShellViewModel(
        Func<MainViewModel> cookbookFactory,
        Func<Recipe, RecipeViewModel> recipeFactory,
        Func<Recipe?, RecipeEditorViewModel> editorFactory,
        Func<Recipe, CookingModeViewModel> cookingModeFactory,
        Func<CategoryManagerViewModel> categoryManagerFactory,
        Func<Guid?, ShareViewModel> shareFactory,
        Func<SettingsViewModel> settingsFactory,
        INearbyShareService nearby)
    {
        _cookbookFactory = cookbookFactory;
        _recipeFactory = recipeFactory;
        _editorFactory = editorFactory;
        _cookingModeFactory = cookingModeFactory;
        _categoryManagerFactory = categoryManagerFactory;
        _shareFactory = shareFactory;
        _settingsFactory = settingsFactory;
        _nearby = nearby;

        _nearby.OfferReceived += OnOfferReceived;
    }

    /// <summary>
    /// The cookbook page is created on first use. Building it inside the constructor would ask the
    /// container for the navigator, which is this very object, while it is still being built.
    /// </summary>
    private MainViewModel Cookbook
    {
        get
        {
            if (_cookbook is null)
            {
                _cookbook = _cookbookFactory();
                _cookbook.PropertyChanged += OnCookbookPropertyChanged;
            }

            return _cookbook;
        }
    }

    public bool HasIncomingOffer => IncomingOffer is not null;

    /// <summary>
    /// A wide window keeps its destinations in a rail that never moves: finding your way back to
    /// the cookbook is chrome, not a step in the recipe. Cooking mode stays immersive.
    /// </summary>
    public bool IsRailVisible => IsWideLayout && Current is not CookingModeViewModel;

    /// <summary>The tab bar belongs to the three top-level pages, not to a recipe being read.</summary>
    public bool IsTabBarVisible => !IsWideLayout && Current is MainViewModel or SettingsViewModel;

    public bool IsCookbookTab => Current is MainViewModel { IsFavoritesActive: false };

    public bool IsFavoritesTab => Current is MainViewModel { IsFavoritesActive: true };

    public bool IsSettingsTab => Current is SettingsViewModel;

    public void ShowCookbook()
    {
        _history.Clear();
        Cookbook.IsFavoritesActive = false;
        Show(Cookbook);
        _ = Cookbook.InitializeAsync();
    }

    public void ShowFavorites()
    {
        _history.Clear();
        Cookbook.IsFavoritesActive = true;
        Show(Cookbook);
        _ = Cookbook.RefreshAsync();
    }

    public void ShowSettings()
    {
        _history.Clear();
        Show(_settingsFactory());
    }

    public void ShowShare(Guid? recipeId = null)
    {
        Push(_shareFactory(recipeId));
    }

    public void ShowRecipe(Recipe recipe) => Push(_recipeFactory(recipe));

    public void ShowEditor(Recipe? existing) => Push(_editorFactory(existing));

    public void ShowCookingMode(Recipe recipe) => Push(_cookingModeFactory(recipe));

    public void ShowCategoryManager() => Push(_categoryManagerFactory());

    public void GoBack()
    {
        if (_history.Count == 0)
        {
            ShowCookbook();
            return;
        }

        var page = _history.Pop();
        Show(page);

        if (page is MainViewModel cookbook)
        {
            _ = cookbook.RefreshAsync();
        }
    }

    private void Push(ViewModelBase page)
    {
        if (Current is not null)
        {
            _history.Push(Current);
        }

        Show(page);
    }

    private void Show(ViewModelBase page)
    {
        Current = page;
        OnPropertyChanged(nameof(IsRailVisible));
        OnPropertyChanged(nameof(IsTabBarVisible));
        OnPropertyChanged(nameof(IsCookbookTab));
        OnPropertyChanged(nameof(IsFavoritesTab));
        OnPropertyChanged(nameof(IsSettingsTab));
    }

    /// <summary>Resizing across the breakpoint swaps one navigation for the other.</summary>
    partial void OnIsWideLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRailVisible));
        OnPropertyChanged(nameof(IsTabBarVisible));
    }

    private void OnCookbookPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsFavoritesActive))
        {
            OnPropertyChanged(nameof(IsCookbookTab));
            OnPropertyChanged(nameof(IsFavoritesTab));
        }
    }

    private void OnOfferReceived(object? sender, IncomingShareOffer offer)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IncomingOffer = offer;
            OfferStatus = null;
            OnPropertyChanged(nameof(HasIncomingOffer));
        });
    }

    [RelayCommand]
    private void ShowCookbookTab() => ShowCookbook();

    [RelayCommand]
    private void ShowFavoritesTab() => ShowFavorites();

    [RelayCommand]
    private void ShowSettingsTab() => ShowSettings();

    [RelayCommand]
    private async System.Threading.Tasks.Task AcceptOffer()
    {
        var offer = IncomingOffer;
        if (offer is null)
        {
            return;
        }

        // The dialog closes straight away; the outcome arrives when the recipes are in.
        IncomingOffer = null;
        OnPropertyChanged(nameof(HasIncomingOffer));
        OfferStatus = $"Receiving from {offer.DeviceName}…";

        var result = await _nearby.RespondAsync(offer.TransferId, accepted: true);

        OfferStatus = result.Message;
        _ = Cookbook.RefreshAsync();
        ClearOfferStatusSoon();
    }

    /// <summary>Status messages are a glance, not a permanent part of the screen.</summary>
    private void ClearOfferStatusSoon()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            OfferStatus = null;
        };
        timer.Start();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeclineOffer()
    {
        var offer = IncomingOffer;
        if (offer is null)
        {
            return;
        }

        await _nearby.RespondAsync(offer.TransferId, accepted: false);
        IncomingOffer = null;
        OnPropertyChanged(nameof(HasIncomingOffer));
        OfferStatus = "Transfer declined.";
        ClearOfferStatusSoon();
    }
}
