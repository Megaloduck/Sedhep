using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Sedhep.Infrastructure;

namespace Sedhep.Tests;

/// <summary>
/// A throwaway cookbook on disk: real SQLite database, real photo folder, real services. Tests only
/// touch their own folder so they can run in parallel.
/// </summary>
internal sealed class TestCookbook : IDisposable
{
    public TestCookbook(string label = "cookbook")
    {
        Root = Path.Combine(AppContext.BaseDirectory, "test-data", $"{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);

        var fixture = new SqliteDatabaseFixture(
            Path.Combine(Root, "cookbook.db"),
            NullLogger<SqliteDatabaseFixture>.Instance);
        Connection = fixture.OpenConnection();

        Recipes = new SqliteRecipeRepository(
            Connection,
            NullLogger<SqliteRecipeRepository>.Instance,
            "test-device");
        Categories = new SqliteCategoryRepository(Connection, NullLogger<SqliteCategoryRepository>.Instance);
        Photos = new FileSystemRecipePhotoStorage(
            Path.Combine(Root, "photos"),
            NullLogger<FileSystemRecipePhotoStorage>.Instance);
        Files = new CookbookFileService(
            Recipes,
            Categories,
            Photos,
            new UtcClock(),
            NullLogger<CookbookFileService>.Instance);
        Settings = new AppSettingsStore(
            Path.Combine(Root, "settings.json"),
            NullLogger<AppSettingsStore>.Instance);
    }

    public string Root { get; }

    public SqliteConnection Connection { get; }

    public SqliteRecipeRepository Recipes { get; }

    public SqliteCategoryRepository Categories { get; }

    public FileSystemRecipePhotoStorage Photos { get; }

    public CookbookFileService Files { get; }

    public AppSettingsStore Settings { get; }

    public int CountRows(string table)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        Connection.Dispose();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A locked file on Windows should not fail the test run.
        }
    }
}
