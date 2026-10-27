using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Sedhep.Infrastructure;

namespace Sedhep.Services;

/// <summary>
/// File pickers are a view concern, so the view models talk to this narrow interface instead of the
/// window. It works for both the desktop window and the single-view mobile hosts.
/// </summary>
public interface IFileDialogService
{
    Task<IStorageFile?> OpenAsync(string title, string extension, string description);

    Task<IStorageFile?> SaveAsync(string title, string suggestedName, string extension, string description);
}

public sealed class FileDialogService : IFileDialogService
{
    public async Task<IStorageFile?> OpenAsync(string title, string extension, string description)
    {
        var topLevel = ResolveTopLevel();
        if (topLevel is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(description) { Patterns = new[] { $"*{extension}" } } }
        });

        return files.Count > 0 ? files[0] : null;
    }

    public async Task<IStorageFile?> SaveAsync(string title, string suggestedName, string extension, string description)
    {
        var topLevel = ResolveTopLevel();
        if (topLevel is null)
        {
            return null;
        }

        return await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType(description) { Patterns = new[] { $"*{extension}" } } }
        });
    }

    private static TopLevel? ResolveTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }
}
