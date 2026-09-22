namespace DesktopEnglish.Services;

/// <summary>Aggregated learning statistics, all computed from the database on demand.</summary>
public sealed record StudyStatistics(
    int TodayStudied,
    int TodayKnown,
    int TodayFuzzy,
    int TodayUnknown,
    int TotalRecords,
    int MasteredWords,
    int DueWords,
    int TotalWords,
    int StreakDays);

/// <summary>Computes learning statistics. Read-only; no caching so numbers always match the database.</summary>
public interface IStatisticsService
{
    Task<StudyStatistics> GetAsync(CancellationToken cancellationToken);
}
