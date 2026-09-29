using DesktopEnglish.Domain;

namespace DesktopEnglish.Services;

/// <summary>
/// Application-level configuration loaded from JSON. These are the <em>defaults</em> applied on first run;
/// afterwards the user's persisted settings take precedence.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    public int ReminderInterval { get; set; } = 30;

    public int DailyWordCount { get; set; } = 20;

    public int WordsPerReminder { get; set; } = 1;

    public bool NotificationEnabled { get; set; } = true;

    public bool SoundEnabled { get; set; } = true;

    public bool AutoStart { get; set; }

    public ReviewAlgorithm ReviewAlgorithm { get; set; } = ReviewAlgorithm.Classic;

    public double FsrsDesiredRetention { get; set; } = 0.9;

    public string Category { get; set; } = "百词斩";

    public int ImportBatchSize { get; set; } = 500;

    public LoggingOptions Logging { get; set; } = new();

    public UserSettingsSnapshot ToSnapshot() => new(
        ReminderInterval: ReminderInterval,
        DailyWordCount: DailyWordCount,
        AutoStart: AutoStart,
        NotificationEnabled: NotificationEnabled,
        SoundEnabled: SoundEnabled,
        WordsPerReminder: WordsPerReminder,
        ReviewAlgorithm: ReviewAlgorithm,
        FsrsDesiredRetention: FsrsDesiredRetention);

    public UserSettings ToSettingsEntity() => new()
    {
        Id = UserSettings.SingletonId,
        ReminderInterval = ReminderInterval,
        DailyWordCount = DailyWordCount,
        AutoStart = AutoStart,
        NotificationEnabled = NotificationEnabled,
        SoundEnabled = SoundEnabled,
        WordsPerReminder = WordsPerReminder,
        ReviewAlgorithm = ReviewAlgorithm,
        FsrsDesiredRetention = FsrsDesiredRetention,
    };
}

/// <summary>Logging configuration driven entirely from JSON.</summary>
public sealed class LoggingOptions
{
    public string MinimumLevel { get; set; } = "Information";

    public int RetainedFileCount { get; set; } = 7;

    public bool WriteToConsole { get; set; } = true;
}
