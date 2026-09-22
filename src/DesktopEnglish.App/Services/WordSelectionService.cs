using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using Microsoft.EntityFrameworkCore;

namespace DesktopEnglish.Services;

/// <summary>
/// Chooses which words the user should study next. Due reviews always come first; new words only fill the
/// remaining daily quota, and the whole batch is capped so the daily limit stays a hard limit.
/// </summary>
public interface IWordSelectionService
{
    /// <summary>Words already studied and now due, oldest due first, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Word>> SelectDueReviewsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Words never studied, easiest first, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Word>> SelectNewWordsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Remaining allowance for today, given the configured daily count. Never negative.</summary>
    Task<int> GetRemainingDailyQuotaAsync(int dailyWordCount, CancellationToken cancellationToken);

    /// <summary>Number of distinct words already studied today (local day).</summary>
    Task<int> GetStudiedTodayCountAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class WordSelectionService(
    IDbContextFactory<AppDbContext> contextFactory,
    IClock clock) : IWordSelectionService
{
    public async Task<IReadOnlyList<Word>> SelectDueReviewsAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var nowUtc = clock.UtcNow;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.WordReviewStates
            .Where(s => s.NextReviewTime <= nowUtc)
            .OrderBy(s => s.NextReviewTime)
            .ThenBy(s => s.WordId)
            .Take(limit)
            .Select(s => s.Word!)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Word>> SelectNewWordsAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Words
            .Where(w => !context.WordReviewStates.Any(s => s.WordId == w.Id))
            .OrderBy(w => w.Level)
            .ThenBy(w => w.Id)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetRemainingDailyQuotaAsync(int dailyWordCount, CancellationToken cancellationToken)
    {
        var studied = await GetStudiedTodayCountAsync(cancellationToken);
        return Math.Max(0, dailyWordCount - studied);
    }

    public async Task<int> GetStudiedTodayCountAsync(CancellationToken cancellationToken)
    {
        var todayLocal = clock.LocalNow.Date;
        var dayStartUtc = clock.ToUtc(todayLocal);
        var dayEndUtc = clock.ToUtc(todayLocal.AddDays(1));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.LearningRecords
            .Where(r => r.ReviewTime >= dayStartUtc && r.ReviewTime < dayEndUtc)
            .Select(r => r.WordId)
            .Distinct()
            .CountAsync(cancellationToken);
    }
}
