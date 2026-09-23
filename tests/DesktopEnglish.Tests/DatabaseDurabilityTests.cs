using System.IO;
using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesktopEnglish.Tests;

/// <summary>
/// Covers the promise that study progress survives closing the app: an answer must be readable again from a
/// brand new connection, every connection must commit durably, and a database file damaged by an interrupted
/// commit must be recovered instead of quietly reported as "no history".
/// </summary>
public sealed class DatabaseDurabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"DesktopEnglish-tests-{Guid.NewGuid():N}");

    public DatabaseDurabilityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover file handle is not worth failing a test over.
        }
    }

    private string DatabasePath => Path.Combine(_root, "desktopenglish.db");

    private string BackupDirectory => Path.Combine(_root, "backup");

    private static DbContextOptions<AppDbContext> OptionsFor(string databasePath) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(SqliteConfiguration.BuildConnectionString(databasePath))
            .AddInterceptors(new SqliteDurabilityInterceptor())
            .Options;

    private DatabaseMaintenanceService CreateMaintenanceService() =>
        new(new StubDbContextFactory(OptionsFor(DatabasePath)), NullLogger<DatabaseMaintenanceService>.Instance);

    [Fact]
    public void Connections_disable_pooling_and_carry_a_busy_timeout()
    {
        var builder = new SqliteConnectionStringBuilder(SqliteConfiguration.BuildConnectionString("C:\\data\\test.db"));

        Assert.False(builder.Pooling);
        Assert.Equal(SqliteConfiguration.BusyTimeoutSeconds, builder.DefaultTimeout);
        Assert.Equal(SqliteCacheMode.Private, builder.Cache);
    }

    [Fact]
    public async Task Every_connection_runs_with_full_sync()
    {
        await using var context = new AppDbContext(OptionsFor(DatabasePath));
        await context.Database.OpenConnectionAsync();

        var connection = context.Database.GetDbConnection();

        Assert.Equal(2L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA synchronous;")));
        Assert.Equal(15000L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA busy_timeout;")));
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "PRAGMA foreign_keys;")));
    }

    [Fact]
    public async Task A_study_answer_is_readable_again_from_a_fresh_connection()
    {
        // Mirrors the user's report: answer a word, close the app, open it again.
        await using (var context = new AppDbContext(OptionsFor(DatabasePath)))
        {
            await context.Database.MigrateAsync();
            context.Words.Add(new Word { Id = 7, WordText = "persist", Level = 1, Category = "test" });
            await context.SaveChangesAsync();
        }

        var service = new StudySessionService(
            new StubDbContextFactory(OptionsFor(DatabasePath)),
            new ReviewScheduler(),
            new FixedClock(),
            new NoOpLogger<StudySessionService>());

        var outcome = await service.RecordResultAsync(7, StudyResult.Known, CancellationToken.None);
        Assert.Equal(1, outcome.Stage);

        // A new context is the equivalent of restarting the app.
        await using var reopened = new AppDbContext(OptionsFor(DatabasePath));
        var state = await reopened.WordReviewStates.SingleAsync(s => s.WordId == 7);

        Assert.Equal(1, state.Stage);
        Assert.Equal(1, state.MasteryLevel);
        Assert.Equal(1, await reopened.LearningRecords.CountAsync(r => r.WordId == 7));
    }

    [Fact]
    public async Task A_healthy_database_is_left_untouched()
    {
        await SeedStudiedWordAsync(1);

        var report = await CreateMaintenanceService()
            .EnsureHealthyAsync(DatabasePath, BackupDirectory, CancellationToken.None);

        Assert.True(report.WasHealthy);
        Assert.False(report.WasRecovered);
        Assert.Null(report.BackupPath);

        await using var context = new AppDbContext(OptionsFor(DatabasePath));
        Assert.Equal(2, (await context.WordReviewStates.SingleAsync()).Stage);
    }

    [Fact]
    public async Task A_truncated_database_is_recovered_together_with_its_history()
    {
        await SeedStudiedWordAsync(1);
        ClaimExtraPages(DatabasePath);

        var report = await CreateMaintenanceService()
            .EnsureHealthyAsync(DatabasePath, BackupDirectory, CancellationToken.None);

        Assert.False(report.WasHealthy);
        Assert.True(report.WasRecovered);
        Assert.NotNull(report.BackupPath);
        Assert.True(File.Exists(report.BackupPath));

        // The study history came across, so the user's progress is not lost.
        await using (var context = new AppDbContext(OptionsFor(DatabasePath)))
        {
            var state = await context.WordReviewStates.SingleAsync();
            Assert.Equal(1, state.WordId);
            Assert.Equal(2, state.Stage);
            Assert.Equal(3, state.CorrectCount);
        }

        // And the file that is now in place is a genuinely healthy database.
        await using var connection = new SqliteConnection(SqliteConfiguration.BuildConnectionString(DatabasePath));
        await connection.OpenAsync();
        Assert.Equal("ok", await ScalarAsync(connection, "PRAGMA quick_check;"));
    }

    [Fact]
    public async Task An_unreadable_file_is_quarantined_and_the_app_starts_fresh()
    {
        await File.WriteAllBytesAsync(DatabasePath, new byte[8192]);

        var report = await CreateMaintenanceService()
            .EnsureHealthyAsync(DatabasePath, BackupDirectory, CancellationToken.None);

        Assert.False(report.WasHealthy);
        Assert.False(report.WasRecovered);
        Assert.True(File.Exists(report.BackupPath));

        // The unusable file is out of the way so the next start can create a clean database.
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Maintenance_pins_the_rollback_journal()
    {
        await SeedStudiedWordAsync(1);

        await using (var connection = new SqliteConnection(SqliteConfiguration.BuildConnectionString(DatabasePath)))
        {
            await connection.OpenAsync();
            Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode=WAL;"));
        }

        var report = await CreateMaintenanceService()
            .EnsureHealthyAsync(DatabasePath, BackupDirectory, CancellationToken.None);
        Assert.True(report.WasHealthy);

        await using var pinned = new SqliteConnection(SqliteConfiguration.BuildConnectionString(DatabasePath));
        await pinned.OpenAsync();
        Assert.Equal("delete", await ScalarAsync(pinned, "PRAGMA journal_mode;"));
    }

    private async Task SeedStudiedWordAsync(int wordId)
    {
        await using var context = new AppDbContext(OptionsFor(DatabasePath));
        await context.Database.MigrateAsync();

        context.Words.Add(new Word { Id = wordId, WordText = $"word{wordId}", Level = 1, Category = "test" });
        context.WordReviewStates.Add(new WordReviewState
        {
            WordId = wordId,
            Stage = 2,
            CorrectCount = 3,
            MasteryLevel = 2,
            NextReviewTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            LastReviewedAt = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc),
        });

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Reproduces the damage an interrupted checkpoint leaves behind: the header claims two more pages than
    /// the file actually holds, which makes SQLite reject the entire database as malformed.
    /// </summary>
    private static void ClaimExtraPages(string databasePath)
    {
        var bytes = File.ReadAllBytes(databasePath);

        var pageSize = (bytes[16] << 8) | bytes[17];
        if (pageSize == 1)
        {
            pageSize = 65536;
        }

        var declared = (uint)(bytes.Length / pageSize) + 2;

        bytes[28] = (byte)(declared >> 24);
        bytes[29] = (byte)(declared >> 16);
        bytes[30] = (byte)(declared >> 8);
        bytes[31] = (byte)declared;

        File.WriteAllBytes(databasePath, bytes);
    }

    private static async Task<object?> ScalarAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
