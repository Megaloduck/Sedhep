using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Infrastructure;
using Sedhep.Navigation;
using Sedhep.Services;

namespace Sedhep.ViewModels;

/// <summary>
/// The Settings tab: how the app looks, where the cookbook lives, and the few housekeeping actions
/// the plan allows. Deliberately short — no accounts, no sync jargon.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettingsStore _settings;
    private readonly AppearanceService _appearance;
    private readonly IRecipeRepository _recipes;
    private readonly ICategoryRepository _categories;
    private readonly FileSystemAppSandbox _sandbox;
    private readonly ICookbookNavigator _navigator;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private string _recipeCountLabel = "…";

    [ObservableProperty]
    private string _categoryCountLabel = "…";

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private int _textStep;

    public SettingsViewModel(
        AppSettingsStore settings,
        AppearanceService appearance,
        IRecipeRepository recipes,
        ICategoryRepository categories,
        FileSystemAppSandbox sandbox,
        ICookbookNavigator navigator,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _appearance = appearance;
        _recipes = recipes;
        _categories = categories;
        _sandbox = sandbox;
        _navigator = navigator;
        _logger = logger;

        DeviceName = settings.Current.DeviceName;
        TextStep = Array.IndexOf(AppearanceService.ScaleSteps, settings.Current.TextScale);
        if (TextStep < 0) TextStep = 0;

        _ = LoadCountsAsync();
    }

    public string VersionText =>
        typeof(SettingsViewModel).Assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "1.0";

    public string StoragePath => _sandbox.DatabasePath;

    public bool IsSystemTheme => _settings.Current.Theme == "system";

    public bool IsLightTheme => _settings.Current.Theme == "light";

    public bool IsDarkTheme => _settings.Current.Theme == "dark";

    public bool IsTextNormal => TextStep == 0;

    public bool IsTextLarge => TextStep == 1;

    public bool IsTextLargest => TextStep == 2;

    private async Task LoadCountsAsync()
    {
        try
        {
            var recipes = await _recipes.GetAllAsync();
            var categories = await _categories.GetAllAsync();
            RecipeCountLabel = recipes.Count == 1 ? "1 recipe" : $"{recipes.Count} recipes";
            CategoryCountLabel = categories.Count == 1 ? "1 category" : $"{categories.Count} categories";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not count the cookbook");
            RecipeCountLabel = "—";
            CategoryCountLabel = "—";
        }
    }

    [RelayCommand]
    private void UseSystemTheme() => ApplyTheme("system");

    [RelayCommand]
    private void UseLightTheme() => ApplyTheme("light");

    [RelayCommand]
    private void UseDarkTheme() => ApplyTheme("dark");

    private void ApplyTheme(string theme)
    {
        _appearance.SetTheme(theme);
        NotifyTheme();
    }

    private void NotifyTheme()
    {
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    [RelayCommand]
    private void UseSmallText() => ApplyTextScale(0);

    [RelayCommand]
    private void UseLargeText() => ApplyTextScale(1);

    [RelayCommand]
    private void UseLargestText() => ApplyTextScale(2);

    private void ApplyTextScale(int step)
    {
        TextStep = step;
        _appearance.SetTextScale(AppearanceService.ScaleSteps[step]);
        OnPropertyChanged(nameof(IsTextNormal));
        OnPropertyChanged(nameof(IsTextLarge));
        OnPropertyChanged(nameof(IsTextLargest));
        Status = "Text size updated";
    }

    [RelayCommand]
    private void SaveDeviceName()
    {
        var name = DeviceName.Trim();
        if (name.Length == 0)
        {
            DeviceName = _settings.Current.DeviceName;
            Status = "A device name cannot be empty";
            return;
        }

        if (name == _settings.Current.DeviceName)
        {
            return;
        }

        _settings.Current.DeviceName = name;
        _settings.Save(_settings.Current);
        Status = "Saved. Other phones will see this name.";
    }

    [RelayCommand]
    private void OpenCategories() => _navigator.ShowCategoryManager();

    [RelayCommand]
    private void OpenShare() => _navigator.ShowShare();
}
