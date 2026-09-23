using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Data;

/// <summary>Outcome of the startup database health check.</summary>
/// <param name="WasHealthy">True when the existing file passed its integrity check.</param>
/// <param name="WasRecovered">True when a damaged file was repaired and its rows copied into a fresh file.</param>
/// <param name="BackupPath">Where the damaged file was preserved, when there was one.</param>
public sealed record DatabaseMaintenanceReport(bool WasHealthy, bool WasRecovered, string? BackupPath);

/// <summary>
/// Runs before anything else touches the database and makes "the app forgot my progress" impossible to hit
/// silently.
/// <para>
/// A damaged SQLite file used to surface as every query failing with "database disk image is malformed",
/// while the user's answers kept appearing to be accepted. This service checks the file on every start,
/// keeps a copy of anything that fails, then rebuilds a clean database from whatever rows are still readable
/// so the study history survives. Only if even that fails does the app start with a fresh file - and the
/// damaged one is still on disk for manual inspection.
/// </para>
/// </summary>
public sealed class DatabaseMaintenanceService(
    IDbContextFactory<AppDbContext> contextFactory,
    ILogger<DatabaseMaintenanceService> logger)
{
    private const int HeaderPageCountOffset = 28;
    private const int HeaderPageSizeOffset = 16;
    private static readonly byte[] SqliteMagic = "SQLite format 3\0"u8.ToArray();

    /// <summary>Verifies the application database, recovering it when necessary.</summary>
    public Task<DatabaseMaintenanceReport> EnsureHealthyAsync(CancellationToken cancellationToken) =>
        EnsureHealthyAsync(AppPaths.DatabasePath, AppPaths.BackupDirectory, cancellationToken);

    /// <summary>Verifies (and recovers) the database file at <paramref name="databasePath"/>.</summary>
    public async Task<DatabaseMaintenanceReport> EnsureHealthyAsync(
        string databasePath,
        string backupDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            logger.LogInformation("No database at {DatabasePath}; creating a new one.", databasePath);
            return new DatabaseMaintenanceReport(WasHealthy: true, WasRecovered: false, BackupPath: null);
        }

        var (isHealthy, detail) = await QuickCheckAsync(databasePath, cancellationToken);
        if (isHealthy)
        {
            PinJournalMode(databasePath);
            return new DatabaseMaintenanceReport(WasHealthy: true, WasRecovered: false, BackupPath: null);
        }

        logger.LogError(
            "Database {DatabasePath} failed its integrity check ({Detail}). Preserving it and attempting recovery.",
            databasePath,
            detail);

        var backupPath = Quarantine(databasePath, backupDirectory);
        logger.LogWarning("Damaged database preserved at {BackupPath}.", backupPath);

        try
        {
            if (await TryRecoverAsync(databasePath, backupPath, backupDirectory, cancellationToken) &&
                await VerifyLiveDatabaseAsync(cancellationToken))
            {
                PinJournalMode(databasePath);
                logger.LogWarning(
                    "Database recovered from {BackupPath}; study history was preserved.",
                    backupPath);

                return new DatabaseMaintenanceReport(false, true, backupPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database recovery failed unexpectedly.");
        }

        logger.LogError(
            "Recovery did not produce a usable database. Starting with a fresh one; the damaged file is kept at {BackupPath}.",
            backupPath);

        DiscardDatabase(databasePath);
        return new DatabaseMaintenanceReport(WasHealthy: false, WasRecovered: false, backupPath);
    }

    /// <summary>
    /// Switches the file to the rollback journal so an interrupted commit is repaired from the journal
    /// instead of leaving the main file with a page count that no longer matches its size.
    /// </summary>
    private void PinJournalMode(string databasePath)
    {
        try
        {
            var mode = SqliteConfiguration.UseRollbackJournal(databasePath);

            if (string.Equals(mode, "delete", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("Database journal mode is {Mode}.", mode);
            }
            else
            {
                logger.LogWarning(
                    "Could not switch the database to rollback journal mode; SQLite reported '{Mode}'.",
                    mode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to pin the database journal mode.");
        }
    }

    /// <summary>Reads the whole file with SQLite's cheap consistency check.</summary>
    private static async Task<(bool IsHealthy, string Detail)> QuickCheckAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(SqliteConfiguration.BuildConnectionString(databasePath));
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var result = await command.ExecuteScalarAsync(cancellationToken) as string;

            return (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase), result ?? "(no result)");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Copies the damaged database next to itself, including a write-ahead log that still holds data.</summary>
    private static string Quarantine(string databasePath, string backupDirectory)
    {
        Directory.CreateDirectory(backupDirectory);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupDirectory, $"desktopenglish-damaged-{stamp}.db");

        File.Copy(databasePath, backupPath, overwrite: true);

        // A non-empty write-ahead log holds commits that never reached the main file, so it is part of the backup.
        CopyIfNotEmpty(databasePath + "-wal", backupPath + "-wal");

        return backupPath;
    }

    /// <summary>Rebuilds a clean database from a damaged one and swaps it into place.</summary>
    private async Task<bool> TryRecoverAsync(
        string databasePath,
        string backupPath,
        string backupDirectory,
        CancellationToken cancellationToken)
    {
        var workDirectory = Path.Combine(backupDirectory, "recovery-" + Path.GetFileNameWithoutExtension(backupPath));
        Directory.CreateDirectory(workDirectory);

        var sourcePath = Path.Combine(workDirectory, "damaged.db");
        File.Copy(backupPath, sourcePath, overwrite: true);
        CopyIfNotEmpty(backupPath + "-wal", sourcePath + "-wal");

        if (!await CanReadAsync(sourcePath, cancellationToken) && !TryRepairPageCount(sourcePath))
        {
            logger.LogWarning("The damaged database cannot be opened and its header could not be repaired.");
            return false;
        }

        if (!await CanReadAsync(sourcePath, cancellationToken))
        {
            logger.LogWarning("The damaged database is still unreadable after repairing its header.");
            return false;
        }

        var recoveredPath = Path.Combine(workDirectory, "recovered.db");
        DeleteIfExists(recoveredPath);
        DeleteIfExists(recoveredPath + "-wal");
        DeleteIfExists(recoveredPath + "-shm");

        await CreateSchemaAsync(recoveredPath, cancellationToken);
        await CopyUserDataAsync(sourcePath, recoveredPath, cancellationToken);

        var (isHealthy, detail) = await QuickCheckAsync(recoveredPath, cancellationToken);
        if (!isHealthy)
        {
            logger.LogWarning("The recovered database did not pass its own integrity check ({Detail}).", detail);
            return false;
        }

        SwapDatabase(databasePath, recoveredPath);
        return true;
    }

    /// <summary>
    /// The shape of this corruption: the header claims more pages than the file physically holds, because a
    /// checkpoint wrote the header before the file grew to match. SQLite then rejects the whole file, even
    /// though every remaining page is intact. Rewriting the page count to the real value makes the surviving
    /// pages readable again. Returns false when the file does not have that shape.
    /// </summary>
    private bool TryRepairPageCount(string databasePath)
    {
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(databasePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the damaged database for header repair.");
            return false;
        }

        if (bytes.Length < 100 || !bytes.AsSpan(0, SqliteMagic.Length).SequenceEqual(SqliteMagic))
        {
            return false;
        }

        int pageSize = ReadBigEndianUInt16(bytes, HeaderPageSizeOffset);
        if (pageSize == 1)
        {
            // 65536 does not fit the 16-bit field, so SQLite stores it as 1.
            pageSize = 65536;
        }

        if (pageSize < 512 || pageSize > 65536 || (pageSize & (pageSize - 1)) != 0)
        {
            return false;
        }

        var actualPages = (uint)(bytes.Length / pageSize);
        if (actualPages == 0)
        {
            return false;
        }

        var declaredPages = ReadBigEndianUInt32(bytes, HeaderPageCountOffset);
        if (declaredPages <= actualPages)
        {
            // Not the truncated-tail shape; leave the file alone.
            return false;
        }

        var usableLength = checked((int)(actualPages * pageSize));
        WriteBigEndianUInt32(bytes, HeaderPageCountOffset, actualPages);

        if (usableLength == bytes.Length)
        {
            File.WriteAllBytes(databasePath, bytes);
        }
        else
        {
            // Drop a partially written trailing page; it is not usable in any case.
            using var stream = new FileStream(databasePath, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, usableLength);
        }

        logger.LogWarning(
            "Repaired the database header: it claimed {DeclaredPages} pages but the file holds {ActualPages}.",
            declaredPages,
            actualPages);

        return true;
    }

    private async Task CreateSchemaAsync(string databasePath, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(SqliteConfiguration.BuildConnectionString(databasePath))
            .AddInterceptors(new SqliteDurabilityInterceptor())
            .Options;

        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private async Task CopyUserDataAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        // Foreign keys are off because rows are copied table by table, so a child table may be filled before
        // its parent. The copies are verified by the integrity check that follows.
        await using var connection = new SqliteConnection(
            SqliteConfiguration.BuildConnectionString(targetPath, foreignKeys: false));
        await connection.OpenAsync(cancellationToken);

        await using (var attach = connection.CreateCommand())
        {
            attach.CommandText = $"ATTACH DATABASE '{sourcePath.Replace("'", "''")}' AS damaged;";
            await attach.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var table in new[] { "UserSettings", "Words", "LearningRecords", "WordReviewStates" })
        {
            await CopyTableAsync(connection, table, cancellationToken);
        }
    }

    private async Task CopyTableAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        try
        {
            using var transaction = connection.BeginTransaction();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO main.{table} SELECT * FROM damaged.{table};";
            var copied = await command.ExecuteNonQueryAsync(cancellationToken);

            transaction.Commit();

            logger.LogInformation("Recovered {Count} row(s) into {Table}.", copied, table);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not recover table {Table}; its rows are lost and will be re-seeded.", table);
        }
    }

    /// <summary>Replaces the database file with the recovered one and clears the stale journal files.</summary>
    private static void SwapDatabase(string databasePath, string recoveredPath)
    {
        var staged = databasePath + ".recovered";
        File.Copy(recoveredPath, staged, overwrite: true);

        // A leftover write-ahead log or shared-memory file belongs to the file being replaced and must never
        // be replayed against the new one.
        DeleteIfExists(databasePath + "-wal");
        DeleteIfExists(databasePath + "-shm");

        File.Move(staged, databasePath, overwrite: true);
    }

    private static void DiscardDatabase(string databasePath)
    {
        DeleteIfExists(databasePath);
        DeleteIfExists(databasePath + "-wal");
        DeleteIfExists(databasePath + "-shm");
    }

    /// <summary>Reads the new file back through the application's own context factory.</summary>
    private async Task<bool> VerifyLiveDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            var words = await context.Words.CountAsync(cancellationToken);
            var states = await context.WordReviewStates.CountAsync(cancellationToken);
            var records = await context.LearningRecords.CountAsync(cancellationToken);

            logger.LogInformation(
                "Recovered database verified: words={Words} reviewStates={States} learningRecords={Records}.",
                words,
                states,
                records);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The recovered database could not be read back.");
            return false;
        }
    }

    private static async Task<bool> CanReadAsync(string databasePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(SqliteConfiguration.BuildConnectionString(databasePath));
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
            await command.ExecuteScalarAsync(cancellationToken);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void CopyIfNotEmpty(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath) && new FileInfo(sourcePath).Length > 0)
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static ushort ReadBigEndianUInt16(byte[] bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private static uint ReadBigEndianUInt32(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24) |
        ((uint)bytes[offset + 1] << 16) |
        ((uint)bytes[offset + 2] << 8) |
        bytes[offset + 3];

    private static void WriteBigEndianUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
