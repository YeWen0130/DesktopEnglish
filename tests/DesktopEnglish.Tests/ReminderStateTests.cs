using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DesktopEnglish.Tests;

/// <summary>Verifies that reminder pause state survives service recreation, matching an app restart.</summary>
public class ReminderStateTests
{
    [Fact]
    public async Task Pause_survives_settings_service_recreation()
    {
        await using var db = await TestDatabase.CreateAsync();
        var factory = new SettingsServiceFactory(db.Factory, new AppOptions());

        var first = factory.Create();
        await first.Settings.LoadAsync(CancellationToken.None);
        await first.Reminder.PauseAsync(CancellationToken.None);

        Assert.True(first.Reminder.IsPaused);
        Assert.True(first.Settings.Current.RemindersPaused);

        var second = factory.Create();
        await second.Settings.LoadAsync(CancellationToken.None);

        Assert.True(second.Settings.Current.RemindersPaused);
        Assert.True(second.Reminder.IsPaused);
    }

    [Fact]
    public async Task Resume_is_persisted_too()
    {
        await using var db = await TestDatabase.CreateAsync();
        db.Context.UserSettings.Add(new UserSettings { RemindersPaused = true });
        await db.Context.SaveChangesAsync();

        var factory = new SettingsServiceFactory(db.Factory, new AppOptions());
        var scope = factory.Create();

        await scope.Settings.LoadAsync(CancellationToken.None);
        await scope.Reminder.ResumeAsync(CancellationToken.None);

        Assert.False(scope.Reminder.IsPaused);
        Assert.False(scope.Settings.Current.RemindersPaused);

        await using var check = await db.Factory.CreateDbContextAsync(CancellationToken.None);
        var stored = await check.UserSettings.SingleAsync(CancellationToken.None);
        Assert.False(stored.RemindersPaused);
    }

    private sealed record SettingsScope(SettingsService Settings, ReminderStateService Reminder);

    private sealed class SettingsServiceFactory(IDbContextFactory<Data.AppDbContext> contextFactory, AppOptions options)
    {
        public SettingsScope Create()
        {
            var settings = new SettingsService(contextFactory, options, new NoOpLogger<SettingsService>());
            var reminder = new ReminderStateService(settings);
            return new SettingsScope(settings, reminder);
        }
    }
}
