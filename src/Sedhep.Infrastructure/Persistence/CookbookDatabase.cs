// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Sedhep.Infrastructure;

/// <summary>
/// Owns the one connection to the cookbook. Keeping the connection behind a lazy holder matters on
/// the startup error screen: the folder may be unusable, and showing the recovery UI must not try
/// to open the database. The relocation code can also checkpoint a database that is already open
/// without forcing one into existence.
/// </summary>
public sealed class CookbookDatabase : IDisposable
{
    private readonly Lazy<SqliteConnection> _connection;
    private readonly ILogger<CookbookDatabase> _logger;

    public CookbookDatabase(
        string path,
        ILogger<SqliteDatabaseFixture> fixtureLogger,
        ILogger<CookbookDatabase> logger)
    {
        _logger = logger;
        _connection = new Lazy<SqliteConnection>(
            () => new SqliteDatabaseFixture(path, fixtureLogger).OpenConnection(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public SqliteConnection Connection => _connection.Value;

    public bool IsOpen => _connection.IsValueCreated;

    /// <summary>
    /// Folds the write-ahead log back into <c>cookbook.db</c> so a copy of the file is complete.
    /// A database that is not open yet needs no checkpoint.
    /// </summary>
    public void TryCheckpoint()
    {
        if (!_connection.IsValueCreated)
        {
            return;
        }

        try
        {
            using var command = _connection.Value.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // A checkpoint is a convenience for copying; the copy still copies the WAL beside the
            // database, so a failure here is not fatal.
            _logger.LogWarning(ex, "Could not checkpoint the cookbook before copying it");
        }
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
        }
    }
}
