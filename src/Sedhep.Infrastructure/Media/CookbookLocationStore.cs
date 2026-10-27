// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sedhep.Infrastructure;

/// <summary>Where the cookbook folder came from, so the UI can explain why it is in use.</summary>
public enum CookbookLocationSource
{
    /// <summary>%LOCALAPPDATA%\Sedhep, the folder Sedhep has always used.</summary>
    Default,

    /// <summary>A folder the user picked in the app; remembered across starts.</summary>
    Saved,

    /// <summary>The SEDHEP_DATA_DIR environment variable.</summary>
    Environment,

    /// <summary>The --data-dir argument for this one run.</summary>
    CommandLine
}

public sealed record CookbookLocation(string Directory, CookbookLocationSource Source);

/// <summary>
/// Remembers a cookbook folder the user chose, outside the cookbook itself. The note cannot live in
/// the default cookbook folder (that is the folder that may be unwritable), and a profile can also
/// be locked down, so several known locations are tried. Every writable copy is kept in step; the
/// newest readable one wins. The last resort is the app's own folder or the temp folder, which is
/// still better than making the user choose again on every start.
/// </summary>
public sealed class CookbookLocationStore
{
    private const string FileName = "cookbook-location.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _lock = new();
    private readonly IReadOnlyList<string> _files;
    private bool _loaded;
    private string? _saved;

    public CookbookLocationStore(string? filePath = null, string? defaultDataDirectory = null)
        : this(
            string.IsNullOrWhiteSpace(filePath) ? DefaultLocationFilePaths() : new[] { filePath },
            defaultDataDirectory)
    {
    }

    public CookbookLocationStore(IReadOnlyList<string> filePaths, string? defaultDataDirectory = null)
    {
        if (filePaths.Count == 0)
        {
            throw new ArgumentException("At least one location-file path is required.", nameof(filePaths));
        }

        _files = filePaths;
        LocationFilePath = filePaths[0];

        DefaultDataDirectory = string.IsNullOrWhiteSpace(defaultDataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sedhep")
            : defaultDataDirectory;
    }

    /// <summary>The preferred note path, shown in the UI.</summary>
    public string LocationFilePath { get; }

    public string DefaultDataDirectory { get; }

    /// <summary>Null when the user has never chosen a folder (or every note is unreadable).</summary>
    public string? SavedDirectory
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _saved;
            }
        }
    }

    /// <summary>
    /// Writes the choice to every note that accepts it. Throws only when nowhere could be written:
    /// promising a folder has been remembered and then losing it on restart would be worse than
    /// telling the user the folder could not be saved.
    /// </summary>
    public void Remember(string directory)
    {
        var normalized = CookbookFolder.Normalize(directory);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A cookbook folder is required.", nameof(directory));
        }

        lock (_lock)
        {
            var json = JsonSerializer.Serialize(new StoredLocation { DataDirectory = normalized }, JsonOptions);
            Exception? lastError = null;
            var wrote = false;

            foreach (var path in _files)
            {
                try
                {
                    WriteNote(path, json);
                    wrote = true;
                }
                catch (Exception ex)
                {
                    lastError ??= ex;
                }
            }

            if (!wrote)
            {
                throw new IOException(
                    "Sedhep could not write its folder note anywhere it is allowed to.",
                    lastError);
            }

            _saved = normalized;
            _loaded = true;
        }
    }

    public void Forget()
    {
        lock (_lock)
        {
            foreach (var path in _files)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception)
                {
                    // Best effort: a note that survives is better than refusing to reset.
                }
            }

            _saved = null;
            _loaded = true;
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        // Every writable note is written together, but one may be stale (for example a roaming
        // note that could be read but not overwritten). The newest valid note is the truth; the
        // preferred path wins only when two notes have the same timestamp.
        string? newestDirectory = null;
        var newestWrite = DateTime.MinValue;

        foreach (var path in _files)
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var stored = JsonSerializer.Deserialize<StoredLocation>(File.ReadAllText(path), JsonOptions);
                var normalized = CookbookFolder.Normalize(stored?.DataDirectory);
                if (normalized.Length == 0)
                {
                    continue;
                }

                var write = File.GetLastWriteTimeUtc(path);
                if (newestDirectory is null || write > newestWrite)
                {
                    newestDirectory = normalized;
                    newestWrite = write;
                }
            }
            catch (Exception)
            {
                // A damaged note is not worth a startup failure: try the next location.
            }
        }

        _saved = newestDirectory;
    }

    private static void WriteNote(string path, string json)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The preferred note: %APPDATA%\Sedhep\cookbook-location.json on Windows.</summary>
    public static string DefaultLocationFilePath() => DefaultLocationFilePaths()[0];

    /// <summary>
    /// Every place a note may live, most preferred first. Paths are local to this machine; the
    /// cookbook itself stays portable, the note is only a pointer.
    /// </summary>
    public static IReadOnlyList<string> DefaultLocationFilePaths()
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
            {
                paths.Add(path);
            }
        }

        Add(CombineUnder(Environment.SpecialFolder.ApplicationData, "Sedhep", FileName));
        Add(CombineUnder(Environment.SpecialFolder.LocalApplicationData, "Sedhep", FileName));
        Add(Path.Combine(AppContext.BaseDirectory, FileName));
        Add(Path.Combine(Path.GetTempPath(), "Sedhep", FileName));

        return paths;
    }

    private static string? CombineUnder(Environment.SpecialFolder folder, params string[] parts)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var all = new string[parts.Length + 1];
        all[0] = root;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return Path.Combine(all);
    }

    private sealed class StoredLocation
    {
        public string? DataDirectory { get; set; }
    }
}

/// <summary>
/// Decides which folder a start uses. Precedence is deliberate:
/// <c>--data-dir</c> for one run, then the folder the user chose in the app, then
/// <c>SEDHEP_DATA_DIR</c>, then the default. The chosen folder is the user's decision and survives
/// an environment variable that was set for some earlier experiment.
/// </summary>
public static class CookbookLocationResolver
{
    public static CookbookLocation Resolve(
        string? commandLineDirectory,
        string? savedDirectory,
        string? environmentDirectory,
        string defaultDirectory)
    {
        var commandLine = CookbookFolder.Normalize(commandLineDirectory);
        if (commandLine.Length > 0)
        {
            return new CookbookLocation(commandLine, CookbookLocationSource.CommandLine);
        }

        var saved = CookbookFolder.Normalize(savedDirectory);
        if (saved.Length > 0)
        {
            return new CookbookLocation(saved, CookbookLocationSource.Saved);
        }

        var environment = CookbookFolder.Normalize(environmentDirectory);
        if (environment.Length > 0)
        {
            return new CookbookLocation(environment, CookbookLocationSource.Environment);
        }

        return new CookbookLocation(CookbookFolder.Normalize(defaultDirectory), CookbookLocationSource.Default);
    }
}
