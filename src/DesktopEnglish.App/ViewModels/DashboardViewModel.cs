using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopEnglish.Services;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.ViewModels;

/// <summary>Shows the learning statistics computed from the database.</summary>
public sealed partial class DashboardViewModel(
    IStatisticsService statistics,
    IClock clock,
    ILogger<DashboardViewModel> logger) : ObservableObject, IAsyncInitializable
{
    [ObservableProperty]
    private int _todayStudied;

    [ObservableProperty]
    private int _todayKnown;

    [ObservableProperty]
    private int _todayFuzzy;

    [ObservableProperty]
    private int _todayUnknown;

    [ObservableProperty]
    private int _totalRecords;

    [ObservableProperty]
    private int _masteredWords;

    [ObservableProperty]
    private int _dueWords;

    [ObservableProperty]
    private int _totalWords;

    [ObservableProperty]
    private int _streakDays;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public async Task InitializeAsync(CancellationToken cancellationToken) => await RefreshAsync(cancellationToken);

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;

        try
        {
            var stats = await statistics.GetAsync(cancellationToken);

            TodayStudied = stats.TodayStudied;
            TodayKnown = stats.TodayKnown;
            TodayFuzzy = stats.TodayFuzzy;
            TodayUnknown = stats.TodayUnknown;
            TotalRecords = stats.TotalRecords;
            MasteredWords = stats.MasteredWords;
            DueWords = stats.DueWords;
            TotalWords = stats.TotalWords;
            StreakDays = stats.StreakDays;

            StatusMessage = $"更新于 {clock.LocalNow:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
            // Page was dismissed; nothing to report.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load statistics.");
            StatusMessage = "统计数据加载失败，请查看日志。";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
