using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sedhep.Infrastructure;

namespace Sedhep.Services;

/// <summary>What the UI needs to know about a folder before it becomes the cookbook folder.</summary>
public sealed record CookbookFolderState(
    string Directory,
    string? Problem,
    bool HasCookbook,
    bool IsCurrent,
    long DatabaseBytes,
    bool DatabaseLooksValid);

/// <summary>The outcome of trying to move the cookbook to another folder.</summary>
public sealed record CookbookLocationChange(bool Success, string? Message, bool Copied);

/// <summary>
/// The desktop storage rules: inspect a candidate folder, copy the current cookbook into it when
/// asked, and remember the choice. Restarting is the dialog's job once it has closed.
/// </summary>
public sealed class CookbookLocationService
{
    private readonly FileSystemAppSandbox _sandbox;
    private readonly CookbookLocationStore _store;
    private readonly CookbookDatabase _database;
    private readonly ILogger<CookbookLocationService> _logger;

    public CookbookLocationService(
        FileSystemAppSandbox sandbox,
        CookbookLocationStore store,
        CookbookDatabase database,
        ILogger<CookbookLocationService> logger)
    {
        _sandbox = sandbox;
        _store = store;
        _database = database;
        _logger = logger;
    }

    public string CurrentDirectory => _sandbox.RootDirectory;

    public string CurrentDatabasePath => _sandbox.DatabasePath;

    public bool CurrentHasCookbook => File.Exists(_sandbox.DatabasePath);

    public CookbookLocationSource CurrentSource => _sandbox.Location.Source;

    public string DefaultDirectory => _store.DefaultDataDirectory;

    public string RememberPath => _store.LocationFilePath;

    public bool IsUsingDefault => CookbookFolder.SamePath(CurrentDirectory, DefaultDirectory);

    /// <summary>Plain words for the pill on the settings and error screens.</summary>
    public string CurrentSourceText => DescribeSource(CurrentSource);

    public static string DescribeSource(CookbookLocationSource source) => source switch
    {
        CookbookLocationSource.CommandLine => "Set with --data-dir for this run",
        CookbookLocationSource.Saved => "Chosen by you",
        CookbookLocationSource.Environment => "Set by SEDHEP_DATA_DIR",
        _ => "Default folder"
    };

    /// <summary>
    /// Looks at a folder without changing anything. The writability probe is the same one startup
    /// uses, so "it looks fine here" means it will open.
    /// </summary>
    public CookbookFolderState Inspect(string directory)
    {
        var full = CookbookFolder.Normalize(directory);
        if (full.Length == 0)
        {
            return new CookbookFolderState(directory, "That is not a usable folder path.", false, false, 0, false);
        }

        var isCurrent = CookbookFolder.SamePath(full, CurrentDirectory);
        var hasCookbook = File.Exists(CookbookFolder.DatabaseFile(full));

        var problem = CookbookFolder.Probe(full);
        if (problem is not null)
        {
            return new CookbookFolderState(full, problem, hasCookbook, isCurrent, 0, false);
        }

        long length = 0;
        var looksValid = true;

        if (hasCookbook)
        {
            try
            {
                length = new FileInfo(CookbookFolder.DatabaseFile(full)).Length;
            }
            catch (Exception)
            {
                length = 0;
            }

            // An empty file is how SQLite starts; a file with the wrong bytes is a different kind
            // of mistake and worth catching before the app restarts into it.
            looksValid = length == 0 || CookbookFolder.LooksLikeCookbook(CookbookFolder.DatabaseFile(full));
            if (!looksValid)
            {
                problem = "The cookbook.db in this folder is not a Sedhep cookbook (it is not an SQLite database).";
            }
        }

        return new CookbookFolderState(full, problem, hasCookbook, isCurrent, length, looksValid);
    }

    /// <summary>
    /// Copies the current cookbook when asked, then remembers the folder. Nothing is moved or
    /// deleted: the old folder keeps every recipe, and a failed copy leaves the current cookbook
    /// untouched.
    /// </summary>
    public async Task<CookbookLocationChange> ApplyAsync(string directory, bool copyCurrent)
    {
        var target = CookbookFolder.Normalize(directory);
        if (target.Length == 0)
        {
            return new CookbookLocationChange(false, "Choose a folder first.", false);
        }

        if (CookbookFolder.SamePath(target, CurrentDirectory))
        {
            return new CookbookLocationChange(false, "Sedhep is already using that folder.", false);
        }

        var problem = CookbookFolder.Probe(target);
        _logger.LogDebug("Folder probe for {Target}: {Problem}", target, problem ?? "writable");
        if (problem is not null)
        {
            return new CookbookLocationChange(false, problem, false);
        }

        var targetHasCookbook = CookbookFolder.HasCookbook(target);
        if (copyCurrent && targetHasCookbook)
        {
            return new CookbookLocationChange(
                false,
                "That folder already has a cookbook, so Sedhep will open it as it is. " +
                "Choose an empty folder to copy this cookbook into.",
                false);
        }

        if (copyCurrent)
        {
            if (!File.Exists(_sandbox.DatabasePath))
            {
                return new CookbookLocationChange(false, "There is no cookbook in the current folder to copy.", false);
            }

            // Make the database file complete before reading it from another thread.
            _database.TryCheckpoint();

            try
            {
                await Task.Run(() => CookbookFolder.Copy(CurrentDirectory, target));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not copy the cookbook from {Source} to {Target}", CurrentDirectory, target);
                return new CookbookLocationChange(
                    false,
                    $"Sedhep could not copy the cookbook to that folder. Nothing was deleted and your current " +
                    $"cookbook is unchanged. ({ex.Message})",
                    false);
            }
        }

        try
        {
            _logger.LogDebug("Remembering the cookbook folder {Target}", target);
            _store.Remember(target);
            _logger.LogDebug("Remembered the cookbook folder {Target}", target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not remember the cookbook folder {Target}", target);
            return new CookbookLocationChange(
                false,
                $"Sedhep could not remember that folder, so it would forget it on restart. ({ex.Message})",
                copyCurrent);
        }

        return new CookbookLocationChange(true, null, copyCurrent);
    }

    /// <summary>
    /// Points Sedhep back at the folder it uses by default. The default is remembered explicitly so
    /// an environment variable left over from another run cannot pull the app elsewhere.
    /// </summary>
    public Task<CookbookLocationChange> UseDefaultAsync(bool copyCurrent)
        => ApplyAsync(DefaultDirectory, copyCurrent);
}
