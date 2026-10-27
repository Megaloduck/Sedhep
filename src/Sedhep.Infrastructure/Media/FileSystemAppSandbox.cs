// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;

namespace Sedhep.Infrastructure;

public sealed class FileSystemAppSandbox
{
    public string DatabasePath { get; }
    public string PhotosDirectory { get; }

    public FileSystemAppSandbox()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(appData, "Sedhep");
        DatabasePath = Path.Combine(root, "cookbook.db");
        PhotosDirectory = Path.Combine(root, "photos");
    }
}
