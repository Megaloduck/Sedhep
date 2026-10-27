using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sedhep.Services;

namespace Sedhep.ViewModels;

/// <summary>
/// The desktop "where should the cookbook live" dialog. It shows the current folder, lets the user
/// pick another one, checks it, and offers to copy the existing cookbook across so moving a
/// cookbook is a deliberate, explained action rather than a side effect.
/// </summary>
public partial class CookbookLocationViewModel : ViewModelBase
{
    private readonly CookbookLocationService _locations;
    private readonly ILogger<CookbookLocationViewModel> _logger;

    [ObservableProperty]
    private string _currentFolder;

    [ObservableProperty]
    private string _currentSourceText;

    [ObservableProperty]
    private string _rememberText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    private string? _targetFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetSummary))]
    private string? _targetSummary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetProblem))]
    private string? _targetProblem;

    [ObservableProperty]
    private bool _showCopyOption;

    [ObservableProperty]
    private bool _copyCurrent;

    [ObservableProperty]
    private bool _canUseTarget;

    [ObservableProperty]
    private bool _canUseDefault;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private bool _saved;

    public CookbookLocationViewModel(
        CookbookLocationService locations,
        ILogger<CookbookLocationViewModel> logger)
    {
        _locations = locations;
        _logger = logger;

        _currentFolder = locations.CurrentDirectory;
        _currentSourceText = locations.CurrentSourceText;
        _rememberText = "Sedhep remembers this choice for the next start.";
        _canUseDefault = !locations.IsUsingDefault;
    }

    /// <summary>Raised when the dialog should close. <see cref="Saved"/> says whether to restart.</summary>
    public event EventHandler? CloseRequested;

    public bool HasTarget => !string.IsNullOrWhiteSpace(TargetFolder);

    public bool HasTargetSummary => !string.IsNullOrWhiteSpace(TargetSummary);

    public bool HasTargetProblem => !string.IsNullOrWhiteSpace(TargetProblem);

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    /// <summary>Where the note normally lives; shown as a tooltip, not as body text.</summary>
    public string RememberPath => _locations.RememberPath;

    /// <summary>Where the picker should open: the folder already chosen, or the current one.</summary>
    public string SuggestedStartPath =>
        !string.IsNullOrWhiteSpace(TargetFolder) ? TargetFolder! : CurrentFolder;

    /// <summary>
    /// Checks a folder the user picked. Called by the view once the native picker returns.
    /// </summary>
    public void Inspect(string directory)
    {
        var state = _locations.Inspect(directory);

        Saved = false;
        Error = null;
        TargetFolder = state.Directory;

        if (state.Problem is not null)
        {
            _logger.LogInformation("Folder {Folder} cannot be used: {Problem}", state.Directory, state.Problem);
            TargetSummary = null;
            TargetProblem = state.Problem;
            ShowCopyOption = false;
            CopyCurrent = false;
            CanUseTarget = false;
            return;
        }

        if (state.IsCurrent)
        {
            TargetSummary = "This is already the folder Sedhep uses.";
            TargetProblem = null;
            ShowCopyOption = false;
            CopyCurrent = false;
            CanUseTarget = false;
            return;
        }

        TargetProblem = null;
        TargetSummary = Describe(state);
        ShowCopyOption = !state.HasCookbook && _locations.CurrentHasCookbook;
        CopyCurrent = ShowCopyOption;
        CanUseTarget = true;
    }

    /// <summary>
    /// Reports a native picker failure on the dialog instead of letting an async void handler take
    /// the process down.
    /// </summary>
    public void ReportPickerFailure(Exception exception)
    {
        _logger.LogError(exception, "The cookbook folder picker could not be opened");
        Error = $"Sedhep could not open the folder picker. {exception.Message}";
    }

    [RelayCommand(CanExecute = nameof(CanUseTarget))]
    private async Task UseThisFolder()
    {
        if (string.IsNullOrWhiteSpace(TargetFolder))
        {
            return;
        }

        _logger.LogInformation(
            "Switching the cookbook folder to {Folder} (copy current: {Copy})",
            TargetFolder,
            CopyCurrent);

        await ApplyAsync(() => _locations.ApplyAsync(TargetFolder!, CopyCurrent));
    }

    [RelayCommand(CanExecute = nameof(CanUseDefault))]
    private async Task UseDefault()
    {
        // Copying into the default folder is not offered: it either already holds the cookbook or
        // the user can pick "Choose a folder" and copy there.
        await ApplyAsync(() => _locations.UseDefaultAsync(copyCurrent: false));
    }

    private async Task ApplyAsync(Func<Task<CookbookLocationChange>> apply)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = null;

        try
        {
            var change = await apply();
            if (!change.Success)
            {
                _logger.LogWarning("The cookbook folder was not changed: {Message}", change.Message);
                Error = change.Message;
                return;
            }

            Saved = true;

            // The window refuses to close while a copy is running. This one has finished, so let
            // the close through before raising the event; otherwise the dialog stays open.
            IsBusy = false;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // A failed move is a sentence on the dialog, never a reason to take the app down.
            _logger.LogError(ex, "Could not change the cookbook folder");
            Error = $"Sedhep could not change the folder. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCanUseTargetChanged(bool value) => UseThisFolderCommand.NotifyCanExecuteChanged();

    partial void OnCanUseDefaultChanged(bool value) => UseDefaultCommand.NotifyCanExecuteChanged();

    private static string Describe(CookbookFolderState state)
    {
        if (!state.HasCookbook)
        {
            return "This folder is empty. Sedhep will create a new cookbook here.";
        }

        if (state.DatabaseBytes <= 0)
        {
            return "This folder has an empty cookbook.db. Sedhep will set it up here.";
        }

        return $"This folder already has a cookbook ({FormatSize(state.DatabaseBytes)}). Sedhep will open it.";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / (1024d * 1024d));
        }

        if (bytes >= 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0} KB", bytes / 1024d);
        }

        return $"{bytes} bytes";
    }
}
