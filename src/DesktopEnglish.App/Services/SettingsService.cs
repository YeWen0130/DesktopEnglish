using System.Text.Json;
using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>
/// Loads default settings from the embedded <c>appsettings.json</c>, then overlays the persisted user
/// settings row stored in SQLite.
/// </summary>
public sealed class SettingsService(
    IDbContextFactory<AppDbContext> contextFactory,
    AppOptions options,
    ILogger<SettingsService> logger) : ISettingsService
{
    private volatile UserSettingsSnapshot _current = options.ToSnapshot();

    public UserSettingsSnapshot Current => _current;

    public event EventHandler<UserSettingsSnapshot>? SettingsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.UserSettings
            .FirstOrDefaultAsync(s => s.Id == UserSettings.SingletonId, cancellationToken);

        if (entity is null)
        {
            entity = options.ToSettingsEntity();
            context.UserSettings.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded default user settings from application defaults.");
        }

        Publish(UserSettingsSnapshot.FromEntity(entity));
    }

    public async Task<UserSettingsSnapshot> UpdateAsync(
        Func<UserSettings, UserSettings> mutate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.UserSettings
            .FirstOrDefaultAsync(s => s.Id == UserSettings.SingletonId, cancellationToken);

        if (entity is null)
        {
            entity = options.ToSettingsEntity();
            context.UserSettings.Add(entity);
        }

        var updated = mutate(entity);
        updated.Id = UserSettings.SingletonId;
        await context.SaveChangesAsync(cancellationToken);

        var snapshot = UserSettingsSnapshot.FromEntity(updated);
        Publish(snapshot);
        return snapshot;
    }

    private void Publish(UserSettingsSnapshot snapshot)
    {
        _current = snapshot;
        SettingsChanged?.Invoke(this, snapshot);
    }
}
