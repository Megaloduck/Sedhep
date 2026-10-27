// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;

namespace Sedhep.Infrastructure;

public sealed class SqliteCategoryRepository : ICategoryRepository
{
    private readonly SqliteConnection _conn;
    private readonly ILogger<SqliteCategoryRepository> _logger;

    public SqliteCategoryRepository(SqliteConnection conn, ILogger<SqliteCategoryRepository> logger)
    {
        _conn = conn;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, SortOrder FROM Categories ORDER BY SortOrder, Name COLLATE NOCASE";
        var list = new List<Category>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new Category
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                SortOrder = reader.GetInt32(2)
            });
        }

        return list;
    }

    public async Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, SortOrder FROM Categories WHERE Id = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Category
        {
            Id = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            SortOrder = reader.GetInt32(2)
        };
    }

    public async Task SaveAsync(Category category, CancellationToken cancellationToken = default)
    {
        if (category.SortOrder <= 0)
        {
            category.SortOrder = await NextSortOrderAsync(cancellationToken);
        }

        _logger.LogDebug("Saving category {CategoryId} name={Name}", category.Id, category.Name);

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Categories (Id, Name, SortOrder)
            VALUES (@id, @name, @order)
            ON CONFLICT(Id) DO UPDATE SET
                Name = @name,
                SortOrder = @order";
        cmd.Parameters.AddWithValue("@id", category.Id.ToString());
        cmd.Parameters.AddWithValue("@name", category.Name);
        cmd.Parameters.AddWithValue("@order", category.SortOrder);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Deleting a category must never delete the recipes filed under it: they are moved to
        // "uncategorised" first, in the same transaction as the delete.
        await using var tx = _conn.BeginTransaction();
        try
        {
            await using (var detach = _conn.CreateCommand())
            {
                detach.Transaction = tx;
                detach.CommandText = "UPDATE Recipes SET CategoryId = @none WHERE CategoryId = @id";
                detach.Parameters.AddWithValue("@none", Guid.Empty.ToString());
                detach.Parameters.AddWithValue("@id", id.ToString());
                await detach.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var delete = _conn.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM Categories WHERE Id = @id";
                delete.Parameters.AddWithValue("@id", id.ToString());
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            tx.Commit();
            _logger.LogInformation("Deleted category {CategoryId}", id);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<int> CountRecipesAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Recipes WHERE CategoryId = @id";
        cmd.Parameters.AddWithValue("@id", categoryId.ToString());
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private async Task<int> NextSortOrderAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM Categories";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
