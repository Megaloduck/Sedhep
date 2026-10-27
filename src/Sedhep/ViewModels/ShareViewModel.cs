using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Infrastructure;
using Sedhep.Navigation;
using Sedhep.Services;

namespace Sedhep.ViewModels;

/// <summary>
/// Sending recipes to another phone and moving cookbook files in and out. The page never shows
/// technical words: a device is "Rina's phone", a transfer is "Send".
/// </summary>
public partial class ShareViewModel : ViewModelBase
{
    private readonly ICookbookFileService _files;
    private readonly INearbyShareService _nearby;
    private readonly IRecipeRepository _recipes;
    private readonly IFileDialogService _dialogs;
    private readonly ICookbookNavigator _navigator;
    private readonly ILogger<ShareViewModel> _logger;
    private readonly Guid? _recipeId;

    private string _singleTitle = string.Empty;
    private int _cookbookCount;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _hasProgress;

    [ObservableProperty]
    private bool _hasSingleRecipe;

    [ObservableProperty]
    private bool _shareSingle = true;

    [ObservableProperty]
    private string _summary = "Preparing…";

    [ObservableProperty]
    private bool _hasDevices;

    [ObservableProperty]
    private bool _isLooking = true;

    [ObservableProperty]
    private string? _fileStatus;

    [ObservableProperty]
    private bool _isFileWorking;

    public ObservableCollection<NearbyDevice> Devices { get; } = new();

    public ShareViewModel(
        ICookbookFileService files,
        INearbyShareService nearby,
        IRecipeRepository recipes,
        IFileDialogService dialogs,
        ICookbookNavigator navigator,
        ILogger<ShareViewModel> logger,
        Guid? recipeId = null)
    {
        _files = files;
        _nearby = nearby;
        _recipes = recipes;
        _dialogs = dialogs;
        _navigator = navigator;
        _logger = logger;
        _recipeId = recipeId;
        HasSingleRecipe = recipeId is not null;

        _nearby.DevicesChanged += OnDevicesChanged;
        _ = InitializeAsync();
    }

    public string SingleRecipeLabel => string.IsNullOrEmpty(_singleTitle)
        ? "This recipe"
        : $"“{_singleTitle}”";

    public string ScopeHint => ShareSingle && HasSingleRecipe
        ? "Only this recipe is sent. Photos travel with it."
        : $"All {_cookbookCount} recipes, their ingredients and photos are sent.";

    public bool IsCookbookScope => !(ShareSingle && HasSingleRecipe);

    private async Task InitializeAsync()
    {
        try
        {
            var all = await _recipes.GetAllAsync();
            _cookbookCount = all.Count;

            if (_recipeId is { } id)
            {
                var recipe = all.FirstOrDefault(r => r.Id == id);
                if (recipe is not null)
                {
                    _singleTitle = recipe.Title;
                    OnPropertyChanged(nameof(SingleRecipeLabel));
                }
                else
                {
                    HasSingleRecipe = false;
                    ShareSingle = false;
                }
            }
            else
            {
                ShareSingle = false;
            }

            RebuildSummary();
            RefreshDevices();
            UpdateFinding();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not prepare the sharing page");
            Status = "Could not read your cookbook";
        }
    }

    private void RebuildSummary()
    {
        Summary = ShareSingle && HasSingleRecipe
            ? "1 recipe"
            : _cookbookCount == 1 ? "1 recipe" : $"{_cookbookCount} recipes";
        OnPropertyChanged(nameof(ScopeHint));
        OnPropertyChanged(nameof(IsCookbookScope));
    }

    partial void OnShareSingleChanged(bool value)
    {
        RebuildSummary();
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshDevices();
            UpdateFinding();
        });
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        foreach (var device in _nearby.Devices)
        {
            Devices.Add(device);
        }

        HasDevices = Devices.Count > 0;
        IsLooking = _nearby.IsDiscovering;
    }

    private void UpdateFinding()
    {
        IsLooking = _nearby.IsDiscovering && !HasDevices;
    }

    [RelayCommand]
    private void ChooseSingle() => ShareSingle = true;

    [RelayCommand]
    private void ChooseCookbook() => ShareSingle = false;

    [RelayCommand]
    private async Task SendTo(NearbyDevice? device)
    {
        if (device is null || IsSending)
        {
            return;
        }

        IsSending = true;
        HasProgress = true;
        Progress = 2;
        Status = $"Getting the recipes ready for {device.DisplayName}…";

        try
        {
            using var package = new MemoryStream();
            var ids = ShareSingle && HasSingleRecipe && _recipeId is { } id
                ? new List<Guid> { id }
                : null;

            var summary = await _files.WriteAsync(package, ids);
            package.Position = 0;

            var progress = new Progress<ShareProgress>(p =>
            {
                Progress = p.Percent;
                Status = p.Message;
            });

            var result = await _nearby.SendAsync(
                device,
                package,
                ids is null ? "Whole cookbook" : $"Recipe: {_singleTitle}",
                summary.RecipeCount,
                package.Length,
                progress);

            Status = result.Message;
            Progress = result.Success ? 100 : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send the cookbook to {Device}", device.DisplayName);
            Status = "That transfer did not work. Are both phones on the same Wi-Fi?";
        }
        finally
        {
            IsSending = false;
            HasProgress = false;
        }
    }

    [RelayCommand]
    private void RefreshNearby()
    {
        RefreshDevices();
        UpdateFinding();
        Status = Devices.Count > 0
            ? "Found a phone nearby."
            : "Still looking. Keep Sedhep open on the other phone.";
    }

    [RelayCommand]
    private async Task ExportFile()
    {
        if (IsFileWorking)
        {
            return;
        }

        IsFileWorking = true;
        FileStatus = "Choose where to save the file…";

        try
        {
            var file = await _dialogs.SaveAsync(
                "Save a cookbook file",
                $"sedhep-{DateTime.Now:yyyy-MM-dd}{CookbookFileService.FileExtension}",
                CookbookFileService.FileExtension,
                "Sedhep cookbook");

            if (file is null)
            {
                FileStatus = null;
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            var summary = await _files.WriteAsync(stream, null);
            FileStatus = $"Saved {summary.RecipeCount} recipes and {summary.PhotoCount} photos.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cookbook export failed");
            FileStatus = "Could not save the cookbook file.";
        }
        finally
        {
            IsFileWorking = false;
        }
    }

    [RelayCommand]
    private async Task ImportFile()
    {
        if (IsFileWorking)
        {
            return;
        }

        IsFileWorking = true;
        FileStatus = "Choose a cookbook file…";

        try
        {
            var file = await _dialogs.OpenAsync(
                "Open a cookbook file",
                CookbookFileService.FileExtension,
                "Sedhep cookbook");

            if (file is null)
            {
                FileStatus = null;
                return;
            }

            await using var stream = await file.OpenReadAsync();
            var result = await _files.ReadAsync(stream);

            var added = result.Imported + result.Replaced;
            FileStatus = added == 0 && result.Skipped == 0
                ? "That file had no recipes in it."
                : $"{added} recipes added, {result.PhotosRestored} photos restored.";

            if (result.Skipped > 0)
            {
                FileStatus += $" {result.Skipped} kept as they were (newer here).";
            }

            _cookbookCount += result.Imported;
            RebuildSummary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cookbook import failed");
            FileStatus = "That file could not be read as a Sedhep cookbook.";
        }
        finally
        {
            IsFileWorking = false;
        }
    }

    [RelayCommand]
    private void Back() => _navigator.GoBack();
}
