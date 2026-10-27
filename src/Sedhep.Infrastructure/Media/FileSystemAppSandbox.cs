// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;

namespace Sedhep.Infrastructure;

/// <summary>
/// Where the cookbook lives on the device. Keeping the database, photos and preferences under one
/// folder makes the whole cookbook portable: copy the folder, keep the recipes. The folder can be
/// the default one, one the user chose in the app, or one named on the command line.
/// </summary>
public sealed class FileSystemAppSandbox
{
    /// <summary>
    /// Optional override used by portable installs, by automated UI runs, and by anyone whose
    /// normal application-data folder is not writable. Also settable with <c>--data-dir</c>.
    /// </summary>
    public const string DataDirectoryVariable = "SEDHEP_DATA_DIR";

    /// <summary>
    /// Set by the desktop host alongside <see cref="DataDirectoryVariable"/> when <c>--data-dir</c>
    /// is used, so the argument applies to this run only and does not outrank a folder the user
    /// chooses in the app.
    /// </summary>
    public const string OneShotVariable = "SEDHEP_DATA_DIR_ONCE";

    public FileSystemAppSandbox()
        : this(new CookbookLocationStore())
    {
    }

    public FileSystemAppSandbox(CookbookLocationStore store)
    {
        var environment = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        var commandLine = Environment.GetEnvironmentVariable(OneShotVariable) is not null ? environment : null;

        Location = CookbookLocationResolver.Resolve(
            commandLine,
            store.SavedDirectory,
            environment,
            store.DefaultDataDirectory);

        DatabasePath = Path.Combine(RootDirectory, CookbookFolder.DatabaseFileName);
        PhotosDirectory = Path.Combine(RootDirectory, CookbookFolder.PhotosFolderName);

        Problem = CookbookFolder.Probe(RootDirectory);
    }

    public CookbookLocation Location { get; }

    public string RootDirectory => Location.Directory;

    public string DatabasePath { get; }

    public string PhotosDirectory { get; }

    public string SettingsPath => Path.Combine(RootDirectory, CookbookFolder.SettingsFileName);

    /// <summary>Null when the folder is usable; otherwise a sentence explaining what is wrong.</summary>
    public string? Problem { get; }

    /// <summary>
    /// A cookbook that already exists here must never be silently replaced by a new one somewhere
    /// else, so the app reports the problem instead of relocating the user's data.
    /// </summary>
    public bool HasExistingCookbook => File.Exists(DatabasePath);
}
