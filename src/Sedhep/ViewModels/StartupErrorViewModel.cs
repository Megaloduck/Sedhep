using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sedhep.Infrastructure;
using Sedhep.Services;

namespace Sedhep.ViewModels;

/// <summary>
/// The screen shown instead of the cookbook when storage cannot be opened. On the desktop it is a
/// recovery screen, not a dead end: the folder can be copied elsewhere or another folder chosen,
/// and Sedhep restarts into it.
/// </summary>
public partial class StartupErrorViewModel : ViewModelBase
{
    private readonly IDialogService? _dialogs;
    private readonly IDesktopService? _desktop;

    [ObservableProperty]
    private string _copyPathLabel = "Copy path";

    public StartupErrorViewModel(
        Exception exception,
        FileSystemAppSandbox? sandbox,
        IDialogService? dialogs,
        IDesktopService? desktop)
    {
        _dialogs = dialogs;
        _desktop = desktop;

        var storage = exception as CookbookStorageException;

        FolderPath = sandbox?.RootDirectory
            ?? (storage is not null ? Path.GetDirectoryName(storage.Path) ?? storage.Path : string.Empty);

        Message = storage?.Message ?? exception.Message;
        SourceText = sandbox is not null
            ? CookbookLocationService.DescribeSource(sandbox.Location.Source)
            : "Cookbook folder";

        FolderHasCookbook = IsUsablePath(FolderPath) && File.Exists(Path.Combine(FolderPath, CookbookFolder.DatabaseFileName));

        Hint = BuildHint(storage is not null);
    }

    public string Message { get; }

    public string FolderPath { get; }

    public string SourceText { get; }

    public string Hint { get; }

    public bool FolderHasCookbook { get; }

    public bool IsDesktop => _desktop?.IsDesktop == true;

    public bool CanChooseFolder => IsDesktop && _dialogs is not null;

    public bool HasFolder => IsUsablePath(FolderPath) && Directory.Exists(FolderPath);

    public string Footnote =>
        "You can also start Sedhep with --data-dir \"<folder>\" or set SEDHEP_DATA_DIR.";

    private string BuildHint(bool isStorageProblem)
    {
        if (isStorageProblem && IsDesktop)
        {
            return "The folder may belong to another user account, be locked by a sync tool, or be " +
                   "blocked by security software.";
        }

        if (isStorageProblem)
        {
            return "Set SEDHEP_DATA_DIR (or run with --data-dir <folder>) to keep the cookbook somewhere " +
                   "this app is allowed to write, then start Sedhep again.";
        }

        return "This is unexpected. The details are in the log next to the app's output.";
    }

    [RelayCommand(CanExecute = nameof(CanChooseFolder))]
    private async Task ChooseFolder()
    {
        if (_dialogs is null)
        {
            return;
        }

        await _dialogs.ShowCookbookLocationAsync();
    }

    [RelayCommand(CanExecute = nameof(HasFolder))]
    private void OpenFolder() => _desktop?.OpenFolder(FolderPath);

    [RelayCommand]
    private async Task CopyPath()
    {
        if (_desktop is null)
        {
            return;
        }

        await _desktop.CopyTextAsync(FolderPath);
        CopyPathLabel = "Copied";

        await Task.Delay(TimeSpan.FromSeconds(2));
        CopyPathLabel = "Copy path";
    }

    [RelayCommand(CanExecute = nameof(IsDesktop))]
    private async Task TryAgain()
    {
        if (_desktop is not null)
        {
            await _desktop.RestartAsync(keepLocationOverrides: true);
        }
    }

    [RelayCommand(CanExecute = nameof(IsDesktop))]
    private void Close() => _desktop?.Quit();

    private static bool IsUsablePath(string? path)
        => !string.IsNullOrWhiteSpace(path) && CookbookFolder.Normalize(path).Length > 0;
}
