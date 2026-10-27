// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Sedhep.Infrastructure;

public sealed class SqliteDatabaseFixture
{
    private static readonly string[] DefaultCategoryNames =
    {
        "Main Dish",
        "Soup",
        "Vegetable",
        "Snack",
        "Dessert",
        "Drink",
        "Sauce",
        "Breakfast",
        "Other"
    };

    private readonly string _path;
    private readonly ILogger<SqliteDatabaseFixture> _logger;

    public SqliteDatabaseFixture(string path, ILogger<SqliteDatabaseFixture> logger)
    {
        _path = path;
        _logger = logger;
    }

    public SqliteConnection OpenConnection()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        RunMigrations(conn);
        return conn;
    }

    private void RunMigrations(SqliteConnection conn)
    {
        _logger.LogInformation("Initializing SQLite database at {Path}", _path);

        using var tx = conn.BeginTransaction();
        try
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Recipes (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Description TEXT,
                    CategoryId TEXT NOT NULL,
                    Cuisine TEXT NOT NULL,
                    Servings INTEGER NOT NULL DEFAULT 1,
                    PrepMinutes INTEGER NOT NULL DEFAULT 0,
                    CookMinutes INTEGER NOT NULL DEFAULT 0,
                    IsFavorite INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    SourceDeviceId TEXT NOT NULL,
                    Notes TEXT
                );

                CREATE TABLE IF NOT EXISTS Ingredients (
                    Id TEXT NOT NULL PRIMARY KEY,
                    RecipeId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Quantity TEXT NOT NULL,
                    Unit TEXT NOT NULL,
                    FOREIGN KEY (RecipeId) REFERENCES Recipes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS RecipeSteps (
                    Id TEXT NOT NULL PRIMARY KEY,
                    RecipeId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    Instruction TEXT NOT NULL,
                    Minutes INTEGER,
                    FOREIGN KEY (RecipeId) REFERENCES Recipes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS RecipePhotos (
                    Id TEXT NOT NULL PRIMARY KEY,
                    RecipeId TEXT NOT NULL,
                    StorageId TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL DEFAULT 0,
                    Hash TEXT,
                    CapturedAt TEXT NOT NULL,
                    IsCover INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (RecipeId) REFERENCES Recipes(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS Categories (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS SyncMetadata (
                    Id TEXT NOT NULL PRIMARY KEY,
                    LocalDeviceId TEXT NOT NULL,
                    RemoteDeviceId TEXT NOT NULL,
                    RecipeId TEXT NOT NULL,
                    OfferedAt TEXT NOT NULL,
                    AcceptedAt TEXT,
                    PayloadHash TEXT,
                    PayloadBytes INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS Recipes_UpdatedAt ON Recipes(UpdatedAt DESC);
                CREATE INDEX IF NOT EXISTS Recipes_CategoryId ON Recipes(CategoryId);
                CREATE INDEX IF NOT EXISTS Recipes_IsFavorite ON Recipes(IsFavorite) WHERE IsFavorite = 1;
                CREATE INDEX IF NOT EXISTS Ingredients_RecipeId ON Ingredients(RecipeId);
                CREATE INDEX IF NOT EXISTS RecipeSteps_RecipeId ON RecipeSteps(RecipeId);
                CREATE INDEX IF NOT EXISTS RecipePhotos_RecipeId ON RecipePhotos(RecipeId);
                CREATE INDEX IF NOT EXISTS SyncMetadata_RecipeId ON SyncMetadata(RecipeId);
                CREATE INDEX IF NOT EXISTS Categories_SortOrder ON Categories(SortOrder, Name);
            ";
            cmd.ExecuteNonQuery();

            SeedCategories(conn, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Gives a brand new cookbook the categories from the plan so the user is not staring at an
    /// empty list. Existing cookbooks are left alone.
    /// </summary>
    private void SeedCategories(SqliteConnection conn, SqliteTransaction tx)
    {
        using (var count = conn.CreateCommand())
        {
            count.Transaction = tx;
            count.CommandText = "SELECT COUNT(*) FROM Categories";
            if (Convert.ToInt32(count.ExecuteScalar()) > 0)
            {
                return;
            }
        }

        for (var i = 0; i < DefaultCategoryNames.Length; i++)
        {
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO Categories (Id, Name, SortOrder) VALUES (@id, @name, @order)";
            insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("@name", DefaultCategoryNames[i]);
            insert.Parameters.AddWithValue("@order", i + 1);
            insert.ExecuteNonQuery();
        }

        _logger.LogInformation("Seeded {Count} default categories", DefaultCategoryNames.Length);
    }
}
