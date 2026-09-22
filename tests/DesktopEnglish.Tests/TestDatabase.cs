using System.Data.Common;
using DesktopEnglish.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DesktopEnglish.Tests;

/// <summary>
/// A real SQLite database held in memory for one test. The connection stays open for the whole test while
/// the factory hands out fresh contexts per call, exactly like production. The <see cref="Context"/> used
/// for seeding is a separate long-lived instance that the code under test never disposes.
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase(DbConnection connection, AppDbContext context, StubDbContextFactory factory, TestClock clock)
    {
        Connection = connection;
        Context = context;
        Factory = factory;
        Clock = clock;
    }

    public DbConnection Connection { get; }

    /// <summary>Context for test setup; never disposed by the code under test.</summary>
    public AppDbContext Context { get; }

    public StubDbContextFactory Factory { get; }

    public TestClock Clock { get; }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }

    public static async Task<TestDatabase> CreateAsync()
    {
        var clock = new TestClock { };
        clock.SetUtc(new DateTime(2026, 3, 10, 15, 0, 0, DateTimeKind.Utc));

        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var factory = new StubDbContextFactory(options);
        await using (var bootstrap = factory.CreateDbContext())
        {
            await bootstrap.Database.EnsureCreatedAsync();
        }

        var context = new AppDbContext(options);
        return new TestDatabase(connection, context, factory, clock);
    }
}
