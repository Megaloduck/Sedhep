// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Sedhep.Domain;
using Microsoft.Extensions.Logging;

namespace Sedhep.Infrastructure;

public sealed class SqliteRecipeRepository : IRecipeRepository
{
    /// <summary>
    /// Every recipe read goes through this projection so the category name travels with the recipe
    /// and callers never have to look it up separately.
    /// </summary>
    private const string RecipeSelect = @"
            SELECT DISTINCT
                R.Id, R.Title, R.Description, R.CategoryId, C.Name AS CategoryName, R.Cuisine,
                R.Servings, R.PrepMinutes, R.CookMinutes, R.IsFavorite, R.CreatedAt, R.UpdatedAt,
                R.SourceDeviceId, R.Notes, R.Revision, R.Tags
            FROM Recipes R
            LEFT JOIN Categories C ON C.Id = R.CategoryId";

    private readonly SqliteConnection _conn;
    private readonly ILogger<SqliteRecipeRepository> _logger;
    private readonly string _deviceId;

    public SqliteRecipeRepository(SqliteConnection conn, ILogger<SqliteRecipeRepository> logger, string deviceId = "")
    {
        _conn = conn;
        _logger = logger;
        _deviceId = deviceId;
    }

    public async Task<IReadOnlyList<Recipe>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = RecipeSelect + "\n            ORDER BY R.UpdatedAt DESC";
        var list = new List<Recipe>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var recipe = ReadRecipe(reader);
            recipe.Ingredients = await ReadIngredientsAsync(recipe.Id, cancellationToken);
            recipe.Steps = await ReadStepsAsync(recipe.Id, cancellationToken);
            recipe.Photos = await ReadPhotosAsync(recipe.Id, cancellationToken);
            list.Add(recipe);
        }
        return list;
    }

    public async Task<Recipe?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = RecipeSelect + "\n            WHERE R.Id = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var recipe = ReadRecipe(reader);
        recipe.Ingredients = await ReadIngredientsAsync(recipe.Id, cancellationToken);
        recipe.Steps = await ReadStepsAsync(recipe.Id, cancellationToken);
        recipe.Photos = await ReadPhotosAsync(recipe.Id, cancellationToken);
        return recipe;
    }

    public async Task<IReadOnlyList<Recipe>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = RecipeSelect + @"
            LEFT JOIN Ingredients I ON I.RecipeId = R.Id
            LEFT JOIN RecipeSteps S ON S.RecipeId = R.Id
            WHERE R.Title LIKE @q
               OR R.Description LIKE @q
               OR R.Notes LIKE @q
               OR R.Tags LIKE @q
               OR I.Name LIKE @q
               OR S.Instruction LIKE @q
            ORDER BY R.UpdatedAt DESC";
        cmd.Parameters.AddWithValue("@q", $"%{query}%");
        var list = new List<Recipe>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var recipe = ReadRecipe(reader);
            recipe.Ingredients = await ReadIngredientsAsync(recipe.Id, cancellationToken);
            recipe.Steps = await ReadStepsAsync(recipe.Id, cancellationToken);
            recipe.Photos = await ReadPhotosAsync(recipe.Id, cancellationToken);
            list.Add(recipe);
        }
        return list;
    }

    public async Task<IReadOnlyList<Recipe>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = RecipeSelect + "\n            WHERE R.CategoryId = @cat\n            ORDER BY R.Title COLLATE NOCASE ASC";
        cmd.Parameters.AddWithValue("@cat", categoryId.ToString());
        var list = new List<Recipe>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var recipe = ReadRecipe(reader);
            recipe.Ingredients = await ReadIngredientsAsync(recipe.Id, cancellationToken);
            recipe.Steps = await ReadStepsAsync(recipe.Id, cancellationToken);
            recipe.Photos = await ReadPhotosAsync(recipe.Id, cancellationToken);
            list.Add(recipe);
        }
        return list;
    }

    public async Task<IReadOnlyList<Recipe>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = RecipeSelect + "\n            WHERE R.IsFavorite = 1\n            ORDER BY R.UpdatedAt DESC";
        var list = new List<Recipe>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var recipe = ReadRecipe(reader);
            recipe.Ingredients = await ReadIngredientsAsync(recipe.Id, cancellationToken);
            recipe.Steps = await ReadStepsAsync(recipe.Id, cancellationToken);
            recipe.Photos = await ReadPhotosAsync(recipe.Id, cancellationToken);
            list.Add(recipe);
        }
        return list;
    }

    public async Task<IReadOnlyList<Recipe>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = RecipeSelect + "\n            ORDER BY R.UpdatedAt DESC LIMIT @n";
        cmd.Parameters.AddWithValue("@n", count);
        var list = new List<Recipe>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var recipe = ReadRecipe(reader);
            recipe.Ingredients = await ReadIngredientsAsync(recipe.Id, cancellationToken);
            recipe.Steps = await ReadStepsAsync(recipe.Id, cancellationToken);
            recipe.Photos = await ReadPhotosAsync(recipe.Id, cancellationToken);
            list.Add(recipe);
        }
        return list;
    }

    public async Task SaveAsync(Recipe recipe, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Saving recipe {RecipeId} title={Title}", recipe.Id, recipe.Title);
        await using var tx = _conn.BeginTransaction();
        try
        {
            await UpsertRecipeAsync(recipe, tx, cancellationToken);

            // The recipe owns its ingredients, steps and photos, so the stored rows have to end up
            // matching the recipe exactly: rows that are no longer part of it are dropped, and the
            // current ones are written. Both happen in the same transaction as the recipe row.
            await ClearChildrenAsync(recipe.Id, tx, cancellationToken);

            foreach (var ing in recipe.Ingredients)
            {
                await InsertIngredientAsync(ing, tx, cancellationToken);
            }

            foreach (var step in recipe.Steps)
            {
                await InsertStepAsync(step, tx, cancellationToken);
            }

            foreach (var photo in recipe.Photos)
            {
                await InsertPhotoAsync(photo, tx, cancellationToken);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // The row goes, but a tombstone stays behind so synchronization can tell "deleted here"
        // apart from "never seen here".
        await using var tx = _conn.BeginTransaction();
        try
        {
            await using (var tombstone = _conn.CreateCommand())
            {
                tombstone.Transaction = tx;
                tombstone.CommandText = @"
                    INSERT INTO DeletedRecipes (Id, DeletedAt, DeviceId, Revision)
                    VALUES (@id, @deleted, @device, 1)
                    ON CONFLICT(Id) DO UPDATE SET
                        DeletedAt = @deleted,
                        Revision = DeletedRecipes.Revision + 1";
                tombstone.Parameters.AddWithValue("@id", id.ToString());
                tombstone.Parameters.AddWithValue("@deleted", DateTimeOffset.UtcNow.ToString("O"));
                tombstone.Parameters.AddWithValue("@device", _deviceId);
                await tombstone.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM Recipes WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id.ToString());
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private async Task ClearChildrenAsync(Guid recipeId, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            DELETE FROM Ingredients WHERE RecipeId = @rid;
            DELETE FROM RecipeSteps WHERE RecipeId = @rid;
            DELETE FROM RecipePhotos WHERE RecipeId = @rid";
        cmd.Parameters.AddWithValue("@rid", recipeId.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertRecipeAsync(Recipe recipe, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO Recipes (Id, Title, Description, CategoryId, Cuisine, Servings, PrepMinutes, CookMinutes, IsFavorite, CreatedAt, UpdatedAt, SourceDeviceId, Notes, Revision, Tags)
            VALUES (@id, @title, @desc, @cat, @cuisine, @servings, @prep, @cook, @fav, @created, @updated, @source, @notes, 1, @tags)
            ON CONFLICT(Id) DO UPDATE SET
                Title = @title,
                Description = @desc,
                CategoryId = @cat,
                Cuisine = @cuisine,
                Servings = @servings,
                PrepMinutes = @prep,
                CookMinutes = @cook,
                IsFavorite = @fav,
                UpdatedAt = @updated,
                SourceDeviceId = @source,
                Notes = @notes,
                Tags = @tags,
                Revision = Recipes.Revision + 1
            RETURNING Revision";
        cmd.Parameters.AddWithValue("@id", recipe.Id.ToString());
        cmd.Parameters.AddWithValue("@title", recipe.Title);
        cmd.Parameters.AddWithValue("@desc", (object?)recipe.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cat", recipe.CategoryId.ToString());
        cmd.Parameters.AddWithValue("@cuisine", recipe.Cuisine);
        cmd.Parameters.AddWithValue("@servings", recipe.Servings);
        cmd.Parameters.AddWithValue("@prep", recipe.PrepMinutes);
        cmd.Parameters.AddWithValue("@cook", recipe.CookMinutes);
        cmd.Parameters.AddWithValue("@fav", recipe.IsFavorite);
        cmd.Parameters.AddWithValue("@created", recipe.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated", recipe.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@source", recipe.SourceDeviceId);
        cmd.Parameters.AddWithValue("@notes", (object?)recipe.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(recipe.Tags ?? Array.Empty<string>()));

        var stored = await cmd.ExecuteScalarAsync(cancellationToken);
        if (stored is long revision)
        {
            recipe.Revision = (int)revision;
        }
    }

    private async Task InsertIngredientAsync(Ingredient ing, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO Ingredients (Id, RecipeId, SortOrder, Name, Quantity, Unit)
            VALUES (@id, @rid, @order, @name, @qty, @unit)";
        cmd.Parameters.AddWithValue("@id", ing.Id.ToString());
        cmd.Parameters.AddWithValue("@rid", ing.RecipeId.ToString());
        cmd.Parameters.AddWithValue("@order", ing.SortOrder);
        cmd.Parameters.AddWithValue("@name", ing.Name);
        cmd.Parameters.AddWithValue("@qty", ing.Quantity);
        cmd.Parameters.AddWithValue("@unit", ing.Unit);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertStepAsync(RecipeStep step, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO RecipeSteps (Id, RecipeId, SortOrder, Instruction, Minutes)
            VALUES (@id, @rid, @order, @instr, @min)";
        cmd.Parameters.AddWithValue("@id", step.Id.ToString());
        cmd.Parameters.AddWithValue("@rid", step.RecipeId.ToString());
        cmd.Parameters.AddWithValue("@order", step.SortOrder);
        cmd.Parameters.AddWithValue("@instr", step.Instruction);
        cmd.Parameters.AddWithValue("@min", (object?)step.Minutes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertPhotoAsync(RecipePhoto photo, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO RecipePhotos (Id, RecipeId, StorageId, FileName, SizeBytes, Hash, CapturedAt, IsCover)
            VALUES (@id, @rid, @sid, @name, @size, @hash, @cap, @cover)";
        cmd.Parameters.AddWithValue("@id", photo.Id.ToString());
        cmd.Parameters.AddWithValue("@rid", photo.RecipeId.ToString());
        cmd.Parameters.AddWithValue("@sid", photo.StorageId);
        cmd.Parameters.AddWithValue("@name", photo.FileName);
        cmd.Parameters.AddWithValue("@size", photo.SizeBytes);
        cmd.Parameters.AddWithValue("@hash", (object?)photo.Hash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cap", photo.CapturedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@cover", photo.IsCover);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Recipe ReadRecipe(SqliteDataReader reader)
    {
        return new Recipe
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
            CategoryId = Guid.Parse(reader.GetString(reader.GetOrdinal("CategoryId"))),
            CategoryName = reader.IsDBNull(reader.GetOrdinal("CategoryName"))
                ? null
                : reader.GetString(reader.GetOrdinal("CategoryName")),
            Cuisine = reader.GetString(reader.GetOrdinal("Cuisine")),
            Servings = reader.GetInt32(reader.GetOrdinal("Servings")),
            PrepMinutes = reader.GetInt32(reader.GetOrdinal("PrepMinutes")),
            CookMinutes = reader.GetInt32(reader.GetOrdinal("CookMinutes")),
            IsFavorite = reader.GetInt32(reader.GetOrdinal("IsFavorite")) != 0,
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
            SourceDeviceId = reader.GetString(reader.GetOrdinal("SourceDeviceId")),
            Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
            Revision = reader.IsDBNull(reader.GetOrdinal("Revision")) ? 1 : reader.GetInt32(reader.GetOrdinal("Revision")),
            Tags = ReadTags(reader)
        };
    }

    private static List<string> ReadTags(SqliteDataReader reader)
    {
        var ordinal = reader.GetOrdinal("Tags");
        if (reader.IsDBNull(ordinal))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(reader.GetString(ordinal)) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private async Task<List<Ingredient>> ReadIngredientsAsync(Guid recipeId, CancellationToken cancellationToken)
    {
        var list = new List<Ingredient>();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Ingredients WHERE RecipeId = @rid ORDER BY SortOrder";
        cmd.Parameters.AddWithValue("@rid", recipeId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new Ingredient
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                RecipeId = recipeId,
                SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Quantity = reader.GetString(reader.GetOrdinal("Quantity")),
                Unit = reader.GetString(reader.GetOrdinal("Unit"))
            });
        }
        return list;
    }

    private async Task<List<RecipeStep>> ReadStepsAsync(Guid recipeId, CancellationToken cancellationToken)
    {
        var list = new List<RecipeStep>();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RecipeSteps WHERE RecipeId = @rid ORDER BY SortOrder";
        cmd.Parameters.AddWithValue("@rid", recipeId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RecipeStep
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                RecipeId = recipeId,
                SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
                Instruction = reader.GetString(reader.GetOrdinal("Instruction")),
                Minutes = reader.IsDBNull(reader.GetOrdinal("Minutes")) ? null : reader.GetInt32(reader.GetOrdinal("Minutes"))
            });
        }
        return list;
    }

    private async Task<List<RecipePhoto>> ReadPhotosAsync(Guid recipeId, CancellationToken cancellationToken)
    {
        var list = new List<RecipePhoto>();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RecipePhotos WHERE RecipeId = @rid ORDER BY IsCover DESC, CapturedAt DESC";
        cmd.Parameters.AddWithValue("@rid", recipeId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RecipePhoto
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                RecipeId = recipeId,
                StorageId = reader.GetString(reader.GetOrdinal("StorageId")),
                FileName = reader.GetString(reader.GetOrdinal("FileName")),
                SizeBytes = reader.GetInt64(reader.GetOrdinal("SizeBytes")),
                Hash = reader.IsDBNull(reader.GetOrdinal("Hash")) ? null : reader.GetString(reader.GetOrdinal("Hash")),
                CapturedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CapturedAt"))),
                IsCover = reader.GetInt32(reader.GetOrdinal("IsCover")) != 0
            });
        }
        return list;
    }
}
