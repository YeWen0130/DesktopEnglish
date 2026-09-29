using DesktopEnglish.Domain;

namespace DesktopEnglish.Services;

/// <summary>
/// Immutable snapshot of the effective user settings, handed out to consumers so a settings change cannot
/// be observed half-applied.
/// </summary>
public sealed record UserSettingsSnapshot(
    int ReminderInterval,
    int DailyWordCount,
    bool AutoStart,
    bool NotificationEnabled,
    bool SoundEnabled,
    int WordsPerReminder,
    ReviewAlgorithm ReviewAlgorithm,
    double FsrsDesiredRetention)
{
    public TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, ReminderInterval));

    public static UserSettingsSnapshot FromEntity(UserSettings entity) => new(
        ReminderInterval: entity.ReminderInterval,
        DailyWordCount: entity.DailyWordCount,
        AutoStart: entity.AutoStart,
        NotificationEnabled: entity.NotificationEnabled,
        SoundEnabled: entity.SoundEnabled,
        WordsPerReminder: entity.WordsPerReminder,
        ReviewAlgorithm: entity.ReviewAlgorithm,
        FsrsDesiredRetention: entity.FsrsDesiredRetention);
}

/// <summary>
/// Provides the effective settings and persists changes. Values are never hard-coded: JSON supplies the
/// defaults and the database holds the user's overrides.
/// </summary>
public interface ISettingsService
{
    UserSettingsSnapshot Current { get; }

    Task LoadAsync(CancellationToken cancellationToken);

    Task<UserSettingsSnapshot> UpdateAsync(
        Func<UserSettings, UserSettings> mutate,
        CancellationToken cancellationToken);

    event EventHandler<UserSettingsSnapshot>? SettingsChanged;
}
