// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;
using System.Security;
using System.Text;

namespace Sedhep.Infrastructure;

/// <summary>
/// The rules for a cookbook folder. Everything a cookbook needs — the database, the photos — lives
/// in one directory, so the folder itself is the unit the user chooses, moves and backs up.
/// </summary>
public static class CookbookFolder
{
    public const string DatabaseFileName = "cookbook.db";
    public const string PhotosFolderName = "photos";
    public const string SettingsFileName = "settings.json";

    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    /// <summary>Windows paths are compared without case; the other platforms are not.</summary>
    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// A full path with stray quotes trimmed, or an empty string when the text is not a usable
    /// path. Callers show a message rather than letting a bad paste become a crash.
    /// </summary>
    public static string Normalize(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        var trimmed = directory.Trim().Trim('"').Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    public static bool SamePath(string? first, string? second)
    {
        var a = Normalize(first);
        var b = Normalize(second);
        return a.Length > 0 && b.Length > 0 && string.Equals(a, b, PathComparison);
    }

    public static string DatabaseFile(string directory) => Path.Combine(directory, DatabaseFileName);

    public static string PhotosDirectory(string directory) => Path.Combine(directory, PhotosFolderName);

    public static string SettingsFile(string directory) => Path.Combine(directory, SettingsFileName);

    public static bool HasCookbook(string directory) => File.Exists(DatabaseFile(directory));

    /// <summary>
    /// Proves the folder can be created and written to, the way the app will write to it later.
    /// Returns null when it is usable, or one sentence saying what is wrong.
    /// </summary>
    public static string? Probe(string directory)
    {
        var full = Normalize(directory);
        if (full.Length == 0)
        {
            return "That is not a usable folder path.";
        }

        try
        {
            Directory.CreateDirectory(full);

            var probe = Path.Combine(full, $".sedhep-write-check-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException
                                       or ArgumentException or PathTooLongException or SecurityException)
        {
            return DescribeProblem(ex);
        }
    }

    /// <summary>
    /// A database whose first bytes are not SQLite is very likely the wrong file: a `.cookbook`
    /// archive, a photo, or a document. Saying so in the chooser is better than failing at startup.
    /// </summary>
    public static bool LooksLikeCookbook(string databasePath)
    {
        try
        {
            using var stream = File.Open(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[SqliteHeader.Length];
            var read = stream.Read(header, 0, header.Length);
            return read == header.Length && header.AsSpan().SequenceEqual(SqliteHeader);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Copies a cookbook into a folder that does not have one yet. The source is only read, never
    /// moved or deleted, so an interrupted copy cannot cost the user recipes.
    /// </summary>
    public static void Copy(string sourceDirectory, string targetDirectory)
    {
        var source = Normalize(sourceDirectory);
        var target = Normalize(targetDirectory);

        if (source.Length == 0 || target.Length == 0)
        {
            throw new IOException("The cookbook folder is not a usable path.");
        }

        if (string.Equals(source, target, PathComparison))
        {
            throw new IOException("The cookbook is already in that folder.");
        }

        var sourceDatabase = DatabaseFile(source);
        if (!File.Exists(sourceDatabase))
        {
            throw new FileNotFoundException("There is no cookbook in the current folder to copy.", sourceDatabase);
        }

        Directory.CreateDirectory(target);

        var targetDatabase = DatabaseFile(target);
        if (File.Exists(targetDatabase))
        {
            throw new IOException("The chosen folder already contains a cookbook.");
        }

        // The write-ahead log and shared-memory file belong to the database. Copying them together
        // keeps a cookbook that was not cleanly closed recoverable on the other side.
        File.Copy(sourceDatabase, targetDatabase);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = sourceDatabase + suffix;
            if (File.Exists(sidecar))
            {
                File.Copy(sidecar, targetDatabase + suffix, overwrite: true);
            }
        }

        CopyDirectory(PhotosDirectory(source), PhotosDirectory(target));

        var sourceSettings = SettingsFile(source);
        var targetSettings = SettingsFile(target);
        if (File.Exists(sourceSettings) && !File.Exists(targetSettings))
        {
            File.Copy(sourceSettings, targetSettings);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(target);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string DescribeProblem(Exception exception) => exception switch
    {
        UnauthorizedAccessException =>
            "This app is not allowed to write to that folder. Another user account may own it, " +
            "or a security tool may be blocking Sedhep.",
        IOException io when !string.IsNullOrWhiteSpace(io.Message) =>
            $"That folder cannot be written to ({io.Message}).",
        _ =>
            $"That folder cannot be written to ({exception.GetType().Name})."
    };
}
