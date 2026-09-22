using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private string _statusMessage = string.Empty;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Load();
        return Task.CompletedTask;
    }

    private void Load()
    {
        _isLoading = true;

        var current = settings.Current;
        ReminderInterval = current.ReminderInterval;
        DailyWordCount = current.DailyWordCount;
        WordsPerReminder = current.WordsPerReminder;
        NotificationEnabled = current.NotificationEnabled;
        SoundEnabled = current.SoundEnabled;
        AutoStart = startup.IsEnabled();
        IsPaused = reminderState.IsPaused;

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

            await settings.UpdateAsync(entity =>
            {
                entity.ReminderInterval = interval;
                entity.DailyWordCount = daily;
                entity.WordsPerReminder = perRound;
                entity.NotificationEnabled = NotificationEnabled;
                entity.SoundEnabled = SoundEnabled;
                entity.AutoStart = AutoStart;
                return entity;
            }, cancellationToken);

            startup.Apply(AutoStart);

            ReminderInterval = interval;
            DailyWordCount = daily;
            WordsPerReminder = perRound;

            StatusMessage = $"已保存（{DateTime.Now:HH:mm:ss}）";
            logger.LogInformation("Settings saved: interval={Interval} daily={Daily} perRound={PerRound}",
                interval, daily, perRound);
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
    private void Pause()
    {
        reminderState.Pause();
        IsPaused = true;
        StatusMessage = "提醒已暂停";
    }

    [RelayCommand]
    private void Resume()
    {
        reminderState.Resume();
        IsPaused = false;
        StatusMessage = "提醒已恢复";
    }
}
