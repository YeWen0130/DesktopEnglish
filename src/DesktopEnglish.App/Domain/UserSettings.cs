namespace DesktopEnglish.Domain;

/// <summary>
/// Persisted user settings. Stored as a single row with <see cref="Id"/> = <see cref="SingletonId"/>.
/// </summary>
public class UserSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Reminder interval in minutes.</summary>
    public int ReminderInterval { get; set; } = 30;

    /// <summary>Maximum number of distinct words studied per local day before reminders go quiet.</summary>
    public int DailyWordCount { get; set; } = 20;

    /// <summary>Whether the app registers itself to start with Windows.</summary>
    public bool AutoStart { get; set; }

    /// <summary>Whether toast notifications are shown at all.</summary>
    public bool NotificationEnabled { get; set; } = true;

    /// <summary>Whether toast notifications play a sound.</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>How many words a single reminder attempts to surface.</summary>
    public int WordsPerReminder { get; set; } = 1;
}
