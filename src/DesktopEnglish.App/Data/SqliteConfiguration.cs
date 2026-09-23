using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DesktopEnglish.Data;

/// <summary>
/// The single source of truth for how the SQLite database is opened.
/// <para>
/// The application used to rely on SQLite defaults: the file had ended up in WAL mode and every connection
/// used a zero-length busy timeout, so a competing writer failed instantly instead of waiting. WAL keeps the
/// authoritative copy of recent commits in a side file and only folds them into the main database during a
/// checkpoint; if that checkpoint is interrupted (process killed, machine suspended or reset mid-write) the
/// main file can end up with a page count in its header that no longer matches its real size. SQLite then
/// refuses the whole database with "database disk image is malformed" and every answer given since the last
/// intact checkpoint is unreachable - which is exactly the "the app forgot everything after I closed it"
/// report.
/// </para>
/// <para>
/// Everything here makes commits durable and predictable instead: a rollback journal
/// (<c>journal_mode=DELETE</c>) whose recovery is journal-backed rather than checkpoint-backed, a full fsync
/// on every commit (<c>synchronous=FULL</c>), a real busy timeout so a concurrent writer waits instead of
/// failing, and no connection pooling so a database file that has just been recovered or replaced is never
/// shadowed by a stale handle to the old file.
/// </para>
/// </summary>
public static class SqliteConfiguration
{
    /// <summary>How long a writer waits for a competing lock before giving up.</summary>
    public const int BusyTimeoutSeconds = 15;

    /// <summary>
    /// Builds the connection string used by both the running application and design-time tooling.
    /// </summary>
    /// <param name="databasePath">Absolute path of the database file.</param>
    /// <param name="foreignKeys">
    /// Whether to enforce foreign keys on the connection. Only the recovery copy turns this off, and only
    /// because it inserts rows table by table.
    /// </param>
    public static string BuildConnectionString(string databasePath, bool foreignKeys = true)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // No shared cache: a shared page cache is a well-known source of corruption when a process dies.
            Cache = SqliteCacheMode.Private,
            // Pooling would keep a handle to the old file alive after a recovery swapped in a new one.
            Pooling = false,
            // Microsoft.Data.Sqlite turns this into SQLite's own busy handling per command.
            DefaultTimeout = BusyTimeoutSeconds,
            ForeignKeys = foreignKeys,
        };

        return builder.ToString();
    }

    /// <summary>
    /// Pragmas that must hold on every connection. Unlike <c>journal_mode</c> these are per-connection, so
    /// they are re-applied whenever a connection is opened (see <see cref="SqliteDurabilityInterceptor"/>).
    /// </summary>
    public static void ApplyConnectionPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA busy_timeout={BusyTimeoutSeconds * 1000};" +
            "PRAGMA synchronous=FULL;" +
            "PRAGMA foreign_keys=ON;" +
            "PRAGMA temp_store=MEMORY;";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Pins the database to the rollback-journal mode, which recovers an interrupted commit from the journal
    /// file instead of a checkpoint. <c>journal_mode</c> is a property of the file, so setting it once is
    /// enough - but it must happen before any other connection starts a transaction on that file.
    /// </summary>
    /// <returns>The journal mode SQLite reports after the switch, normally <c>"delete"</c>.</returns>
    public static string UseRollbackJournal(string databasePath)
    {
        using var connection = new SqliteConnection(BuildConnectionString(databasePath));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE;";
        return (command.ExecuteScalar() as string) ?? "(unknown)";
    }
}

/// <summary>
/// Re-applies the durability pragmas every time EF Core opens a connection, so a short-lived context can
/// never run without them.
/// </summary>
public sealed class SqliteDurabilityInterceptor : DbConnectionInterceptor
{
    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        SqliteConfiguration.ApplyConnectionPragmas(connection);

    /// <inheritdoc />
    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        SqliteConfiguration.ApplyConnectionPragmas(connection);
        return Task.CompletedTask;
    }
}
