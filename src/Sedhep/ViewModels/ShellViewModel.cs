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
public partial class ShellViewModel : ViewModelBase, ICookbookNavigator, ICookbookImportPolicy
{
    /// <summary>At tablet width, keep the collection beside the selected recipe.</summary>
    public const double SplitBreakpoint = 600;

    /// <summary>At desktop width, move the top-level destinations to the left rail.</summary>
    public const double WideBreakpoint = 1024;

    private readonly Func<MainViewModel> _cookbookFactory;
    private readonly Func<Recipe, RecipeViewModel> _recipeFactory;
    private readonly Func<Recipe?, RecipeEditorViewModel> _editorFactory;
    private readonly Func<Recipe, CookingModeViewModel> _cookingModeFactory;
    private readonly Func<CategoryManagerViewModel> _categoryManagerFactory;
    private readonly Func<AlmanacViewModel> _almanacFactory;
    private readonly Func<Guid?, ShareViewModel> _shareFactory;
    private readonly Func<SettingsViewModel> _settingsFactory;
    private readonly INearbyShareService _nearby;
    private readonly Stack<ViewModelBase> _history = new();

    private MainViewModel? _cookbook;
    private AlmanacViewModel? _almanac;

    [ObservableProperty]
    private ViewModelBase? _current;

    [ObservableProperty]
    private IncomingShareOffer? _incomingOffer;

    [ObservableProperty]
    private string? _offerStatus;

    [ObservableProperty]
    private bool _isWideLayout;

    [ObservableProperty]
    private bool _isSplitLayout;

    [ObservableProperty]
    private RecipeViewModel? _selectedRecipe;

    [ObservableProperty]
    private CookbookConflict? _pendingIncomingConflict;

    [ObservableProperty]
    private string? _incomingConflictDetails;

    private TaskCompletionSource<ConflictDecision>? _incomingConflictAnswer;

    private readonly System.Threading.SemaphoreSlim _incomingConflictGate = new(1, 1);

    public ShellViewModel(
        Func<MainViewModel> cookbookFactory,
        Func<Recipe, RecipeViewModel> recipeFactory,
        Func<Recipe?, RecipeEditorViewModel> editorFactory,
        Func<Recipe, CookingModeViewModel> cookingModeFactory,
        Func<CategoryManagerViewModel> categoryManagerFactory,
        Func<AlmanacViewModel> almanacFactory,
        Func<Guid?, ShareViewModel> shareFactory,
        Func<SettingsViewModel> settingsFactory,
        INearbyShareService nearby)
    {
        _cookbookFactory = cookbookFactory;
        _recipeFactory = recipeFactory;
        _editorFactory = editorFactory;
        _cookingModeFactory = cookingModeFactory;
        _categoryManagerFactory = categoryManagerFactory;
        _almanacFactory = almanacFactory;
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

    /// <summary>Built on first use, then kept: flipping between the cookbook and the almanac is instant.</summary>
    private AlmanacViewModel Almanac
    {
        get
        {
            if (_almanac is null)
            {
                _almanac = _almanacFactory();
                _almanac.PropertyChanged += OnAlmanacPropertyChanged;
            }

            return _almanac;
        }
    }

    public bool HasIncomingOffer => IncomingOffer is not null;

    public bool HasPendingIncomingConflict => PendingIncomingConflict is not null;

    partial void OnPendingIncomingConflictChanged(CookbookConflict? value)
        => OnPropertyChanged(nameof(HasPendingIncomingConflict));

    /// <summary>
    /// A wide window keeps its destinations in a rail that never moves: finding your way back to
    /// the cookbook is chrome, not a step in the recipe. Cooking mode stays immersive.
    /// </summary>
    public bool IsRailVisible => IsWideLayout && Current is not CookingModeViewModel;

    /// <summary>
    /// The tab bar belongs to the top-level pages, not to a recipe being read, and not to an
    /// ingredient card that is open: a dialog covers the app whole, navigation included.
    /// </summary>
    public bool IsTabBarVisible => !IsWideLayout && Current switch
    {
        MainViewModel => true,
        AlmanacViewModel almanac => !almanac.HasSelection,
        SettingsViewModel => true,
        _ => false
    };

    public bool IsSplitViewVisible => IsSplitLayout && Current is MainViewModel;

    public bool IsCollectionTabBarVisible => IsTabBarVisible && IsSplitLayout;

    public bool HasSelectedRecipe => IsSplitViewVisible && SelectedRecipe is not null;

    public int MainContentColumnSpan => IsSplitViewVisible ? 1 : 2;

    public bool IsCookbookTab => Current is MainViewModel { IsFavoritesActive: false };

    public bool IsFavoritesTab => Current is MainViewModel { IsFavoritesActive: true };

    public bool IsAlmanacTab => Current is AlmanacViewModel;

    public bool IsSettingsTab => Current is SettingsViewModel;

    public void ShowCookbook()
    {
        _history.Clear();
        SelectedRecipe = null;
        Cookbook.IsFavoritesActive = false;
        Show(Cookbook);
        _ = Cookbook.InitializeAsync();
    }

    public void ShowFavorites()
    {
        _history.Clear();
        SelectedRecipe = null;
        Cookbook.IsFavoritesActive = true;
        Show(Cookbook);
        _ = Cookbook.RefreshAsync();
    }

    public void ShowAlmanac()
    {
        _history.Clear();
        SelectedRecipe = null;
        Show(Almanac);
        _ = Almanac.RefreshAsync();
    }

    public void ShowSettings()
    {
        _history.Clear();
        SelectedRecipe = null;
        Show(_settingsFactory());
    }

    public void ShowShare(Guid? recipeId = null)
    {
        Push(_shareFactory(recipeId));
    }

    public void ShowRecipe(Recipe recipe)
    {
        if (IsSplitLayout && Current is MainViewModel)
        {
            SelectedRecipe = _recipeFactory(recipe);
            return;
        }

        Push(_recipeFactory(recipe));
    }

    public void ShowEditor(Recipe? existing) => Push(_editorFactory(existing));

    public void ShowCookingMode(Recipe recipe) => Push(_cookingModeFactory(recipe));

    public void ShowCategoryManager() => Push(_categoryManagerFactory());

    public void GoBack()
    {
        if (HasSelectedRecipe)
        {
            SelectedRecipe = null;
            return;
        }

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
        OnPropertyChanged(nameof(IsCollectionTabBarVisible));
        OnPropertyChanged(nameof(IsCookbookTab));
        OnPropertyChanged(nameof(IsFavoritesTab));
        OnPropertyChanged(nameof(IsAlmanacTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(IsSplitViewVisible));
        OnPropertyChanged(nameof(HasSelectedRecipe));
        OnPropertyChanged(nameof(MainContentColumnSpan));
    }

    public void UpdateLayout(double width)
    {
        IsSplitLayout = width >= SplitBreakpoint;
        IsWideLayout = width >= WideBreakpoint;
    }

    partial void OnSelectedRecipeChanged(RecipeViewModel? value) => OnPropertyChanged(nameof(HasSelectedRecipe));

    partial void OnIsSplitLayoutChanged(bool value)
    {
        if (!value && SelectedRecipe is not null && Current is MainViewModel)
        {
            var recipe = SelectedRecipe;
            SelectedRecipe = null;
            Push(recipe);
        }
        else if (value && Current is RecipeViewModel recipe && _history.TryPeek(out var previous) && previous is MainViewModel cookbook)
        {
            _history.Pop();
            SelectedRecipe = recipe;
            Show(cookbook);
        }

        OnPropertyChanged(nameof(IsSplitViewVisible));
        OnPropertyChanged(nameof(IsCollectionTabBarVisible));
        OnPropertyChanged(nameof(HasSelectedRecipe));
        OnPropertyChanged(nameof(MainContentColumnSpan));
    }

    /// <summary>Resizing across the breakpoint swaps one navigation for the other.</summary>
    partial void OnIsWideLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRailVisible));
        OnPropertyChanged(nameof(IsTabBarVisible));
        OnPropertyChanged(nameof(IsCollectionTabBarVisible));
    }

    private void OnAlmanacPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AlmanacViewModel.HasSelection))
        {
            OnPropertyChanged(nameof(IsTabBarVisible));
            OnPropertyChanged(nameof(IsCollectionTabBarVisible));
        }
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
    private void ShowAlmanacTab() => ShowAlmanac();

    [RelayCommand]
    private void ShowSettingsTab() => ShowSettings();

    /// <summary>The create button in the rail and in the bottom bar open the same blank page.</summary>
    [RelayCommand]
    private void NewRecipe() => ShowEditor(null);

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

        var result = await _nearby.RespondAsync(offer.TransferId, accepted: true, this);

        OfferStatus = result.Message;
        _ = Cookbook.RefreshAsync();

        // New ingredients mean the almanac has something new to file.
        _ = _almanac?.RefreshAsync();
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

    /// <summary>
    /// Incoming network imports pause here for a clear choice whenever a recipe already exists.
    /// Keep the interaction on the UI thread while the transfer worker waits for the answer.
    /// </summary>
    public async System.Threading.Tasks.Task<ConflictDecision> ResolveAsync(
        CookbookConflict conflict,
        System.Threading.CancellationToken cancellationToken = default)
    {
        await _incomingConflictGate.WaitAsync(cancellationToken);
        var answer = new TaskCompletionSource<ConflictDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _incomingConflictAnswer = answer;
                IncomingConflictDetails = conflict.LocalRevision == conflict.IncomingRevision
                    ? "Both copies have the same revision. Nothing changes unless you choose the incoming copy."
                    : $"Your cookbook has revision {conflict.LocalRevision}; the incoming copy has revision {conflict.IncomingRevision}. Choose which one to keep.";
                PendingIncomingConflict = conflict;
            });

            return await answer.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_incomingConflictAnswer, answer))
                {
                    PendingIncomingConflict = null;
                    _incomingConflictAnswer = null;
                }
            });
            _incomingConflictGate.Release();
        }
    }

    [RelayCommand]
    private void KeepLocalIncomingConflict() => _incomingConflictAnswer?.TrySetResult(ConflictDecision.KeepLocal);

    [RelayCommand]
    private void UseIncomingConflict() => _incomingConflictAnswer?.TrySetResult(ConflictDecision.UseIncoming);

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
