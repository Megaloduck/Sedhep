using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Sedhep.Services;

/// <summary>
/// The native "choose a folder" sheet, kept in one place. A folder picker is a desktop affordance;
/// the callers only show it when <see cref="IDesktopService.IsDesktop"/> is true.
/// </summary>
public static class FolderPicker
{
    public static async Task<string?> PickAsync(TopLevel owner, string title, string? startDirectory)
    {
        IStorageFolder? start = null;

        if (!string.IsNullOrWhiteSpace(startDirectory) && Directory.Exists(startDirectory))
        {
            try
            {
                start = await owner.StorageProvider.TryGetFolderFromPathAsync(startDirectory);
            }
            catch (Exception)
            {
                // A missing suggested start is not a reason to refuse to open the picker.
            }
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start
        });

        var folder = folders.FirstOrDefault();
        if (folder is null)
        {
            return null;
        }

        var local = folder.TryGetLocalPath();
        return string.IsNullOrWhiteSpace(local) ? folder.Path.LocalPath : local;
    }
}
