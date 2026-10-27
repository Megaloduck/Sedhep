// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;

namespace Sedhep.Infrastructure;

/// <summary>
/// Where the cookbook lives on the device. Keeping the database, photos and preferences under one
/// folder makes the whole cookbook portable: copy the folder, keep the recipes.
/// </summary>
public sealed class FileSystemAppSandbox
{
    /// <summary>
    /// Optional override used by portable installs, by automated UI runs, and by anyone whose
    /// normal application-data folder is not writable. Also settable with <c>--data-dir</c>.
    /// </summary>
    public const string DataDirectoryVariable = "SEDHEP_DATA_DIR";

    public FileSystemAppSandbox()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        RootDirectory = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sedhep")
            : overrideRoot;

        DatabasePath = Path.Combine(RootDirectory, "cookbook.db");
        PhotosDirectory = Path.Combine(RootDirectory, "photos");

        Problem = Probe();
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public string PhotosDirectory { get; }

    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    /// <summary>Null when the folder is usable; otherwise a sentence explaining what is wrong.</summary>
    public string? Problem { get; }

    /// <summary>
    /// A cookbook that already exists here must never be silently replaced by a new one somewhere
    /// else, so the app reports the problem instead of relocating the user's data.
    /// </summary>
    public bool HasExistingCookbook => File.Exists(DatabasePath);

    private string? Probe()
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);

            var probe = Path.Combine(RootDirectory, $".sedhep-write-check-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            return $"The folder is not writable by this app ({ex.GetType().Name}).";
        }
    }
}
