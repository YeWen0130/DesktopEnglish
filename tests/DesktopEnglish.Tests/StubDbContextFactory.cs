using DesktopEnglish.Data;
using Microsoft.EntityFrameworkCore;

namespace DesktopEnglish.Tests;

/// <summary>
/// Mirrors the production <c>IDbContextFactory</c>: every call creates a new context over the same
/// connection, so disposing one context never invalidates the shared database.
/// </summary>
internal sealed class StubDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppDbContext(options));
}
