using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace a2n.Vista.Examples;

/// <summary>
/// The SQLite connection the Northwind sample hosts open, and the one the file-database race tests
/// reuse (Decision Log D160). A file database opened with only <c>Data Source</c> has no busy timeout
/// the write path can rely on: a competing writer gets <c>SQLITE_BUSY</c> (5) or <c>SQLITE_LOCKED</c>
/// (6) instead of waiting for the other transaction to commit and then losing the concurrency predicate.
/// </summary>
internal static class SqliteVistaConnection
{
    /// <summary>How long a writer waits for a SQLite lock before the provider raises a busy error.</summary>
    public const int BusyTimeoutMilliseconds = 5000;

    /// <summary>
    /// Points <paramref name="options"/> at the file <paramref name="databasePath"/> with the sample
    /// busy timeout, WAL, and an immediate write transaction.
    /// </summary>
    /// <param name="options">The context options being built.</param>
    /// <param name="databasePath">The file database to open.</param>
    /// <param name="busyTimeoutMilliseconds">
    /// The busy timeout in milliseconds. The sample hosts use <see cref="BusyTimeoutMilliseconds"/>.
    /// A test that must observe the busy failure passes a tiny value.
    /// </param>
    /// <returns>The same builder, so further interceptors can be added.</returns>
    public static DbContextOptionsBuilder Use(
        DbContextOptionsBuilder options,
        string databasePath,
        int busyTimeoutMilliseconds = BusyTimeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var seconds = busyTimeoutMilliseconds <= 0
            ? 0
            : Math.Max(1, (busyTimeoutMilliseconds + 999) / 1000);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = seconds,
        }.ToString();

        return options
            .UseSqlite(connectionString)
            .AddInterceptors(
                new SqliteVistaConnectionInterceptor(busyTimeoutMilliseconds),
                new SqliteVistaTransactionInterceptor());
    }

    /// <summary>
    /// Applies <c>busy_timeout</c> and WAL when the connection opens.
    /// </summary>
    private sealed class SqliteVistaConnectionInterceptor : DbConnectionInterceptor
    {
        private readonly int _busyTimeoutMilliseconds;

        public SqliteVistaConnectionInterceptor(int busyTimeoutMilliseconds)
        {
            _busyTimeoutMilliseconds = busyTimeoutMilliseconds < 0 ? 0 : busyTimeoutMilliseconds;
        }

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            ApplyPragmas(connection);
            base.ConnectionOpened(connection, eventData);
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
        }

        private void ApplyPragmas(DbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = BusyTimeoutPragma();
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();
        }

        private async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.CommandText = BusyTimeoutPragma();
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            command.CommandText = "PRAGMA journal_mode = WAL;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private string BusyTimeoutPragma() =>
            string.Create(CultureInfo.InvariantCulture, $"PRAGMA busy_timeout = {_busyTimeoutMilliseconds};");
    }

    /// <summary>
    /// Starts each EF transaction with <c>BEGIN IMMEDIATE</c> so a write does not take a shared lock
    /// and then upgrade it.
    /// </summary>
    private sealed class SqliteVistaTransactionInterceptor : DbTransactionInterceptor
    {
        public override InterceptionResult<DbTransaction> TransactionStarting(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result)
        {
            if (connection is SqliteConnection sqlite)
            {
                return InterceptionResult<DbTransaction>.SuppressWithResult(sqlite.BeginTransaction(deferred: false));
            }

            return result;
        }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (connection is SqliteConnection sqlite)
            {
                // The async begin on this provider has no deferred flag. BEGIN IMMEDIATE is one
                // round-trip, so the synchronous call keeps the write lock from being an upgrade.
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(
                    InterceptionResult<DbTransaction>.SuppressWithResult(sqlite.BeginTransaction(deferred: false)));
            }

            return ValueTask.FromResult(result);
        }
    }
}
