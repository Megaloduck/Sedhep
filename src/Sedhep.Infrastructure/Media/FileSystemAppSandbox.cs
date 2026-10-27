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
    /// Optional override used by portable installs and by automated UI runs; when it is not set the
    /// cookbook lives in the normal per-user application data folder.
    /// </summary>
    public const string DataDirectoryVariable = "SEDHEP_DATA_DIR";

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public string PhotosDirectory { get; }

    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");

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

        Directory.CreateDirectory(RootDirectory);
    }
}
