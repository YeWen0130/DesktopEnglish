using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DesktopEnglish.Data;

/// <summary>
/// Used by <c>dotnet ef</c> at design time only. Points at the same user-scoped database file the app uses
/// so generated migrations match production schema.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(SqliteConfiguration.BuildConnectionString(AppPaths.DatabasePath))
            .AddInterceptors(new SqliteDurabilityInterceptor())
            .Options;

        return new AppDbContext(options);
    }
}
