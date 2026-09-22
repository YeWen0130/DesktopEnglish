using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using Microsoft.EntityFrameworkCore;

namespace DesktopEnglish.Services;

/// <summary>
/// Derives statistics from stored study records and review state.
/// "Today" is resolved against the machine's local date, then converted to UTC for the query, so a record
/// made at 00:30 local time counts toward that local day.
/// </summary>
public sealed class StatisticsService(
    IDbContextFactory<AppDbContext> contextFactory,
    IClock clock) : IStatisticsService
{
    public async Task<StudyStatistics> GetAsync(CancellationToken cancellationToken)
    {
        var nowLocal = clock.LocalNow;
        var dayStartUtc = clock.ToUtc(nowLocal.Date);
        var dayEndUtc = clock.ToUtc(nowLocal.Date.AddDays(1));
        var nowUtc = clock.UtcNow;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var todayRecords = context.LearningRecords
            .Where(r => r.ReviewTime >= dayStartUtc && r.ReviewTime < dayEndUtc);

        var todayStudied = await todayRecords
            .Select(r => r.WordId)
            .Distinct()
            .CountAsync(cancellationToken);

        var todayKnown = await todayRecords.CountAsync(r => r.Result == StudyResult.Known, cancellationToken);
        var todayFuzzy = await todayRecords.CountAsync(r => r.Result == StudyResult.Fuzzy, cancellationToken);
        var todayUnknown = await todayRecords.CountAsync(r => r.Result == StudyResult.Unknown, cancellationToken);

        var totalRecords = await context.LearningRecords.CountAsync(cancellationToken);

        var masteredWords = await context.WordReviewStates
            .CountAsync(s => s.MasteryLevel >= 5, cancellationToken);

        var dueWords = await context.WordReviewStates
            .CountAsync(s => s.NextReviewTime <= nowUtc, cancellationToken);

        var totalWords = await context.Words.CountAsync(cancellationToken);

        var streakDays = await ComputeStreakAsync(context, nowLocal.Date, cancellationToken);

        return new StudyStatistics(
            TodayStudied: todayStudied,
            TodayKnown: todayKnown,
            TodayFuzzy: todayFuzzy,
            TodayUnknown: todayUnknown,
            TotalRecords: totalRecords,
            MasteredWords: masteredWords,
            DueWords: dueWords,
            TotalWords: totalWords,
            StreakDays: streakDays);
    }

    /// <summary>
    /// Counts consecutive local days with at least one study record, starting today, or yesterday when the
    /// user has not studied yet today (so an in-progress streak is not reported as broken).
    /// </summary>
    private async Task<int> ComputeStreakAsync(
        AppDbContext context,
        DateTime todayLocal,
        CancellationToken cancellationToken)
    {
        var earliestUtc = clock.ToUtc(todayLocal.AddDays(-400));

        var reviewTimes = await context.LearningRecords
            .Where(r => r.ReviewTime >= earliestUtc)
            .Select(r => r.ReviewTime)
            .ToListAsync(cancellationToken);

        if (reviewTimes.Count == 0)
        {
            return 0;
        }

        var studyDays = reviewTimes
            .Select(utc => clock.ToLocal(utc).Date)
            .ToHashSet();

        var cursor = todayLocal;
        if (!studyDays.Contains(cursor))
        {
            cursor = cursor.AddDays(-1);
            if (!studyDays.Contains(cursor))
            {
                return 0;
            }
        }

        var streak = 0;
        while (studyDays.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }
}
