using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.ViewModels;

/// <summary>Which dashboard detail panel is currently expanded.</summary>
public enum DashboardDetail
{
    None,
    Studied,
    Correct,
    Unknown,
    Calendar,
}

/// <summary>One word studied today with how its answers were split.</summary>
public sealed record StudiedWordItem(
    string WordText,
    string Phonetic,
    string Meaning,
    int Known,
    int Fuzzy,
    int Unknown)
{
    public string Summary => $"认识 {Known} · 模糊 {Fuzzy} · 不认识 {Unknown}";
}

/// <summary>A single cell of the streak calendar; empty cells pad the first and last week.</summary>
public sealed record CalendarCell(string DayText, string Summary, bool IsEmpty, bool HasActivity)
{
    public static CalendarCell Empty { get; } = new(string.Empty, string.Empty, true, false);
}

/// <summary>Shows the learning statistics computed from the database and the clickable detail panels.</summary>
public sealed partial class DashboardViewModel(
    IStatisticsService statistics,
    ILearningRecordService records,
    IClock clock,
    ILogger<DashboardViewModel> logger) : ObservableObject, IAsyncInitializable
{
    private static readonly string[] WeekdayHeaders = ["日", "一", "二", "三", "四", "五", "六"];

    private int _calendarYear;
    private int _calendarMonth;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsStudiedDetail))]
    [NotifyPropertyChangedFor(nameof(IsCorrectDetail))]
    [NotifyPropertyChangedFor(nameof(IsUnknownDetail))]
    [NotifyPropertyChangedFor(nameof(IsCalendarDetail))]
    [NotifyPropertyChangedFor(nameof(DetailTitle))]
    private DashboardDetail _activeDetail = DashboardDetail.None;

    [ObservableProperty]
    private ObservableCollection<StudiedWordItem> _studiedItems = [];

    [ObservableProperty]
    private ObservableCollection<LearningRecordDetail> _correctItems = [];

    [ObservableProperty]
    private ObservableCollection<LearningRecordDetail> _unknownItems = [];

    [ObservableProperty]
    private ObservableCollection<CalendarCell> _calendarCells = [];

    [ObservableProperty]
    private string _calendarTitle = string.Empty;

    public IReadOnlyList<string> Weekdays => WeekdayHeaders;

    public bool IsDetailVisible => ActiveDetail != DashboardDetail.None;

    public bool IsStudiedDetail => ActiveDetail == DashboardDetail.Studied;

    public bool IsCorrectDetail => ActiveDetail == DashboardDetail.Correct;

    public bool IsUnknownDetail => ActiveDetail == DashboardDetail.Unknown;

    public bool IsCalendarDetail => ActiveDetail == DashboardDetail.Calendar;

    public string DetailTitle => ActiveDetail switch
    {
        DashboardDetail.Studied => "今日学习单词",
        DashboardDetail.Correct => "今日正确单词",
        DashboardDetail.Unknown => "今日错误单词",
        DashboardDetail.Calendar => "连续学习日历",
        _ => string.Empty,
    };

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

            if (IsDetailVisible)
            {
                await LoadDetailAsync(cancellationToken);
            }

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

    [RelayCommand]
    private async Task ToggleStudiedAsync(CancellationToken cancellationToken) =>
        await ToggleAsync(DashboardDetail.Studied, cancellationToken);

    [RelayCommand]
    private async Task ToggleCorrectAsync(CancellationToken cancellationToken) =>
        await ToggleAsync(DashboardDetail.Correct, cancellationToken);

    [RelayCommand]
    private async Task ToggleUnknownAsync(CancellationToken cancellationToken) =>
        await ToggleAsync(DashboardDetail.Unknown, cancellationToken);

    [RelayCommand]
    private async Task ToggleCalendarAsync(CancellationToken cancellationToken)
    {
        if (ActiveDetail != DashboardDetail.Calendar)
        {
            var today = clock.LocalNow;
            _calendarYear = today.Year;
            _calendarMonth = today.Month;
        }

        await ToggleAsync(DashboardDetail.Calendar, cancellationToken);
    }

    [RelayCommand]
    private void CloseDetail() => ActiveDetail = DashboardDetail.None;

    [RelayCommand]
    private async Task PreviousMonthAsync(CancellationToken cancellationToken)
    {
        var cursor = new DateTime(_calendarYear, _calendarMonth, 1).AddMonths(-1);
        _calendarYear = cursor.Year;
        _calendarMonth = cursor.Month;
        await LoadDetailAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task NextMonthAsync(CancellationToken cancellationToken)
    {
        var cursor = new DateTime(_calendarYear, _calendarMonth, 1).AddMonths(1);
        _calendarYear = cursor.Year;
        _calendarMonth = cursor.Month;
        await LoadDetailAsync(cancellationToken);
    }

    [RelayCommand]
    private Task MarkKnownAsync(LearningRecordDetail? item, CancellationToken cancellationToken) =>
        AdjustAsync(item, StudyResult.Known, cancellationToken);

    [RelayCommand]
    private Task MarkUnknownAsync(LearningRecordDetail? item, CancellationToken cancellationToken) =>
        AdjustAsync(item, StudyResult.Unknown, cancellationToken);

    private async Task ToggleAsync(DashboardDetail detail, CancellationToken cancellationToken)
    {
        ActiveDetail = ActiveDetail == detail ? DashboardDetail.None : detail;

        if (IsDetailVisible)
        {
            await LoadDetailAsync(cancellationToken);
        }
    }

    private async Task LoadDetailAsync(CancellationToken cancellationToken)
    {
        try
        {
            switch (ActiveDetail)
            {
                case DashboardDetail.Studied:
                case DashboardDetail.Correct:
                case DashboardDetail.Unknown:
                {
                    var today = await records.GetDayAsync(clock.LocalNow.Date, cancellationToken);

                    StudiedItems = new ObservableCollection<StudiedWordItem>(
                        today.GroupBy(r => r.WordId)
                             .Select(g => new StudiedWordItem(
                                 WordText: g.First().WordText,
                                 Phonetic: g.First().Phonetic,
                                 Meaning: g.First().Meaning,
                                 Known: g.Count(r => r.Result == StudyResult.Known),
                                 Fuzzy: g.Count(r => r.Result == StudyResult.Fuzzy),
                                 Unknown: g.Count(r => r.Result == StudyResult.Unknown)))
                             .OrderBy(i => i.WordText));

                    CorrectItems = new ObservableCollection<LearningRecordDetail>(
                        today.Where(r => r.Result == StudyResult.Known));

                    UnknownItems = new ObservableCollection<LearningRecordDetail>(
                        today.Where(r => r.Result == StudyResult.Unknown));

                    break;
                }

                case DashboardDetail.Calendar:
                    await LoadCalendarAsync(cancellationToken);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load dashboard detail.");
            StatusMessage = "详情加载失败，请查看日志。";
        }
    }

    private async Task LoadCalendarAsync(CancellationToken cancellationToken)
    {
        var month = await records.GetMonthAsync(_calendarYear, _calendarMonth, cancellationToken);
        var byDay = month.Days.ToDictionary(d => d.LocalDate.Day);

        var first = new DateTime(_calendarYear, _calendarMonth, 1);
        var daysInMonth = DateTime.DaysInMonth(_calendarYear, _calendarMonth);
        var leading = (int)first.DayOfWeek;

        var cells = new List<CalendarCell>();

        for (var i = 0; i < leading; i++)
        {
            cells.Add(CalendarCell.Empty);
        }

        for (var day = 1; day <= daysInMonth; day++)
        {
            if (byDay.TryGetValue(day, out var activity))
            {
                cells.Add(new CalendarCell(
                    DayText: day.ToString(),
                    Summary: $"{activity.StudiedWords} 词 · 对{activity.Known} 错{activity.Unknown}",
                    IsEmpty: false,
                    HasActivity: true));
            }
            else
            {
                cells.Add(new CalendarCell(day.ToString(), string.Empty, IsEmpty: false, HasActivity: false));
            }
        }

        // Pad the final week so the grid stays rectangular.
        while (cells.Count % 7 != 0)
        {
            cells.Add(CalendarCell.Empty);
        }

        CalendarCells = new ObservableCollection<CalendarCell>(cells);
        CalendarTitle = $"{_calendarYear} 年 {_calendarMonth} 月";
    }

    private async Task AdjustAsync(
        LearningRecordDetail? item,
        StudyResult target,
        CancellationToken cancellationToken)
    {
        if (item is null || item.Result == target)
        {
            return;
        }

        try
        {
            await records.ChangeResultAsync(item.RecordId, target, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to adjust record {RecordId}.", item.RecordId);
            StatusMessage = "调整失败，请查看日志。";
            return;
        }

        await RefreshAsync(cancellationToken);
    }
}
