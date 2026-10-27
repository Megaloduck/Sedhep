using System;
using System.IO;
using System.Text;
using Sedhep.Infrastructure;
using Xunit;

namespace Sedhep.Tests;

/// <summary>
/// The folder rules a desktop user depends on: which folder wins, what "writable" means, and that
/// copying a cookbook never touches the original.
/// </summary>
public sealed class CookbookLocationTests : IDisposable
{
    private readonly string _root;

    public CookbookLocationTests()
    {
        // Normalize once: Windows may hand out an 8.3 short path here, while the location rules
        // (correctly) return the long form of a path.
        _root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "test-data",
            $"location-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void CommandLineFolderWinsOverSavedAndEnvironment()
    {
        var location = CookbookLocationResolver.Resolve(
            Path.Combine(_root, "from-command-line"),
            Path.Combine(_root, "saved"),
            Path.Combine(_root, "environment"),
            Path.Combine(_root, "default"));

        Assert.Equal(Path.Combine(_root, "from-command-line"), location.Directory);
        Assert.Equal(CookbookLocationSource.CommandLine, location.Source);
    }

    [Fact]
    public void SavedFolderWinsOverEnvironmentVariable()
    {
        var location = CookbookLocationResolver.Resolve(
            null,
            Path.Combine(_root, "saved"),
            Path.Combine(_root, "environment"),
            Path.Combine(_root, "default"));

        Assert.Equal(Path.Combine(_root, "saved"), location.Directory);
        Assert.Equal(CookbookLocationSource.Saved, location.Source);
    }

    [Fact]
    public void EnvironmentVariableIsUsedWhenNothingWasChosen()
    {
        var location = CookbookLocationResolver.Resolve(
            null,
            null,
            Path.Combine(_root, "environment"),
            Path.Combine(_root, "default"));

        Assert.Equal(Path.Combine(_root, "environment"), location.Directory);
        Assert.Equal(CookbookLocationSource.Environment, location.Source);
    }

    [Fact]
    public void DefaultFolderIsTheLastFallback()
    {
        var location = CookbookLocationResolver.Resolve(null, null, null, Path.Combine(_root, "default"));

        Assert.Equal(Path.Combine(_root, "default"), location.Directory);
        Assert.Equal(CookbookLocationSource.Default, location.Source);
    }

    [Fact]
    public void RememberedFolderSurvivesANewStoreAndCanBeForgotten()
    {
        var note = Path.Combine(_root, "note", "cookbook-location.json");
        var chosen = Path.Combine(_root, "chosen");

        var store = new CookbookLocationStore(note);
        Assert.Null(store.SavedDirectory);

        store.Remember(chosen);
        Assert.True(File.Exists(note));

        var reloaded = new CookbookLocationStore(note);
        Assert.Equal(chosen, reloaded.SavedDirectory);

        reloaded.Forget();
        Assert.False(File.Exists(note));
        Assert.Null(new CookbookLocationStore(note).SavedDirectory);
    }

    [Fact]
    public void RememberFallsBackWhenThePreferredNoteCannotBeWritten()
    {
        // A file where the preferred note's folder should be makes that path unusable.
        var blocker = Path.Combine(_root, "blocked");
        File.WriteAllText(blocker, "not a folder");
        var preferred = Path.Combine(blocker, "cookbook-location.json");
        var fallback = Path.Combine(_root, "fallback", "cookbook-location.json");
        var chosen = Path.Combine(_root, "chosen-fallback");

        var notes = new[] { preferred, fallback };
        var store = new CookbookLocationStore(notes);
        store.Remember(chosen);

        Assert.False(File.Exists(preferred));
        Assert.True(File.Exists(fallback));
        Assert.Equal(chosen, new CookbookLocationStore(notes).SavedDirectory);
    }

    [Fact]
    public void TheNewestNoteWinsEvenWhenAnOlderOneIsPreferred()
    {
        var preferred = Path.Combine(_root, "preferred", "cookbook-location.json");
        var fallback = Path.Combine(_root, "fallback", "cookbook-location.json");
        var older = Path.Combine(_root, "older-folder");
        var newer = Path.Combine(_root, "newer-folder");

        new CookbookLocationStore(preferred).Remember(older);
        new CookbookLocationStore(fallback).Remember(newer);

        File.SetLastWriteTimeUtc(preferred, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(fallback, DateTime.UtcNow);

        var store = new CookbookLocationStore(new[] { preferred, fallback });
        Assert.Equal(newer, store.SavedDirectory);
    }

    [Fact]
    public void AProbeWritesAndRemovesItsFile()
    {
        var folder = Path.Combine(_root, "writable");

        Assert.Null(CookbookFolder.Probe(folder));
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void AProbeRejectsAPathThatIsAFile()
    {
        var file = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(file, "x");

        Assert.NotNull(CookbookFolder.Probe(file));
    }

    [Fact]
    public void CopyBringsTheDatabasePhotosAndSettingsAndLeavesTheOriginal()
    {
        var source = Path.Combine(_root, "source");
        var target = Path.Combine(_root, "target");

        Directory.CreateDirectory(Path.Combine(source, "photos", "recipe-1"));
        File.WriteAllBytes(Path.Combine(source, "cookbook.db"), Encoding.ASCII.GetBytes("SQLite format 3\0"));
        File.WriteAllText(Path.Combine(source, "cookbook.db-wal"), "wal");
        File.WriteAllText(Path.Combine(source, "photos", "recipe-1", "cover.jpg"), "jpg");
        File.WriteAllText(Path.Combine(source, "settings.json"), "{}");

        CookbookFolder.Copy(source, target);

        Assert.Equal("SQLite format 3\0", File.ReadAllText(Path.Combine(target, "cookbook.db")));
        Assert.Equal("wal", File.ReadAllText(Path.Combine(target, "cookbook.db-wal")));
        Assert.Equal("jpg", File.ReadAllText(Path.Combine(target, "photos", "recipe-1", "cover.jpg")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "settings.json")));

        Assert.True(File.Exists(Path.Combine(source, "cookbook.db")));
        Assert.True(File.Exists(Path.Combine(source, "photos", "recipe-1", "cover.jpg")));
    }

    [Fact]
    public void CopyRefusesToOverwriteAnExistingCookbook()
    {
        var source = Path.Combine(_root, "source");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(source, "cookbook.db"), Encoding.ASCII.GetBytes("SQLite format 3\0"));
        File.WriteAllText(Path.Combine(target, "cookbook.db"), "already here");

        Assert.Throws<IOException>(() => CookbookFolder.Copy(source, target));
        Assert.Equal("already here", File.ReadAllText(Path.Combine(target, "cookbook.db")));
    }

    [Fact]
    public void LooksLikeCookbookChecksTheSqliteHeader()
    {
        var good = Path.Combine(_root, "good.db");
        var bad = Path.Combine(_root, "bad.db");
        File.WriteAllBytes(good, Encoding.ASCII.GetBytes("SQLite format 3\0rest of the file"));
        File.WriteAllText(bad, "not a database");

        Assert.True(CookbookFolder.LooksLikeCookbook(good));
        Assert.False(CookbookFolder.LooksLikeCookbook(bad));
        Assert.False(CookbookFolder.LooksLikeCookbook(Path.Combine(_root, "missing.db")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked file on Windows should not fail the test run.
        }
    }
}
