using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.ViewModels;

/// <summary>
/// Edits the persisted user settings. Changing the interval takes effect immediately because the scheduler
/// subscribes to <see cref="ISettingsService.SettingsChanged"/>.
/// </summary>
public sealed partial class SettingsViewModel(
    ISettingsService settings,
    IReminderStateService reminderState,
    IStartupService startup,
    ILogger<SettingsViewModel> logger) : ObservableObject, IAsyncInitializable
{
    public sealed record ReviewAlgorithmOption(ReviewAlgorithm Value, string Name, string Description);

    public IReadOnlyList<ReviewAlgorithmOption> ReviewAlgorithms { get; } =
    [
        new(ReviewAlgorithm.Classic, "经典固定间隔", "按 1、2、4、7、15、30、60 天复习"),
        new(ReviewAlgorithm.Sm2, "SM-2", "根据回答动态调整难度与间隔"),
        new(ReviewAlgorithm.Fsrs, "FSRS", "根据记忆稳定性预测下一次复习"),
    ];

    /// <summary>Guards against re-entrant saves while the initial values are being applied.</summary>
    private bool _isLoading;

    [ObservableProperty]
    private int _reminderInterval;

    [ObservableProperty]
    private int _dailyWordCount;

    [ObservableProperty]
    private int _wordsPerReminder;

    [ObservableProperty]
    private bool _notificationEnabled;

    [ObservableProperty]
    private bool _soundEnabled;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFsrsSelected))]
    private ReviewAlgorithmOption? _selectedReviewAlgorithm;

    [ObservableProperty]
    private double _fsrsDesiredRetentionPercent = 90;

    public bool IsFsrsSelected => SelectedReviewAlgorithm?.Value == ReviewAlgorithm.Fsrs;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        Load();
        _isLoading = false;
        return Task.CompletedTask;
    }

    private void Load()
    {

        var current = settings.Current;
        ReminderInterval = current.ReminderInterval;
        DailyWordCount = current.DailyWordCount;
        WordsPerReminder = current.WordsPerReminder;
        NotificationEnabled = current.NotificationEnabled;
        SoundEnabled = current.SoundEnabled;
        AutoStart = startup.IsEnabled();
        IsPaused = reminderState.IsPaused;
        SelectedReviewAlgorithm = ReviewAlgorithms.First(option => option.Value == current.ReviewAlgorithm);
        FsrsDesiredRetentionPercent = current.FsrsDesiredRetention * 100;

        _isLoading = false;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            var interval = Math.Clamp(ReminderInterval, 1, 24 * 60);
            var daily = Math.Clamp(DailyWordCount, 1, 1000);
            var perRound = Math.Clamp(WordsPerReminder, 1, 50);
            var algorithm = SelectedReviewAlgorithm?.Value ?? ReviewAlgorithm.Classic;
            var retentionPercent = double.IsFinite(FsrsDesiredRetentionPercent)
                ? Math.Clamp(FsrsDesiredRetentionPercent, 80, 97)
                : 90;

            await settings.UpdateAsync(entity =>
            {
                entity.ReminderInterval = interval;
                entity.DailyWordCount = daily;
                entity.WordsPerReminder = perRound;
                entity.NotificationEnabled = NotificationEnabled;
                entity.SoundEnabled = SoundEnabled;
                entity.AutoStart = AutoStart;
                entity.ReviewAlgorithm = algorithm;
                entity.FsrsDesiredRetention = retentionPercent / 100;
                return entity;
            }, cancellationToken);

            startup.Apply(AutoStart);

            ReminderInterval = interval;
            DailyWordCount = daily;
            WordsPerReminder = perRound;
            FsrsDesiredRetentionPercent = retentionPercent;

            StatusMessage = $"已保存（{DateTime.Now:HH:mm:ss}）";
            logger.LogInformation(
                "Settings saved: interval={Interval} daily={Daily} perRound={PerRound} algorithm={Algorithm} retention={Retention}",
                interval, daily, perRound, algorithm, retentionPercent / 100);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save settings.");
            StatusMessage = "保存失败，请查看日志。";
        }
    }

    [RelayCommand]
    private async Task PauseAsync(CancellationToken cancellationToken) =>
        await SetPausedAsync(true, "提醒已暂停", cancellationToken);

    [RelayCommand]
    private async Task ResumeAsync(CancellationToken cancellationToken) =>
        await SetPausedAsync(false, "提醒已恢复", cancellationToken);

    private async Task SetPausedAsync(bool paused, string message, CancellationToken cancellationToken)
    {
        try
        {
            if (paused)
            {
                await reminderState.PauseAsync(cancellationToken);
            }
            else
            {
                await reminderState.ResumeAsync(cancellationToken);
            }

            IsPaused = paused;
            StatusMessage = message;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to change reminder pause state.");
            StatusMessage = "操作失败，请查看日志。";
        }
    }
}
