using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>A single study record joined with its word, for the dashboard detail panels.</summary>
public sealed record LearningRecordDetail(
    int RecordId,
    int WordId,
    string WordText,
    string Phonetic,
    string Meaning,
    StudyResult Result,
    DateTime ReviewedAtLocal);

/// <summary>Per-day activity for the streak calendar.</summary>
public sealed record DayActivity(
    DateTime LocalDate,
    int StudiedWords,
    int Known,
    int Unknown,
    int Fuzzy);

/// <summary>Daily activity for one local calendar month.</summary>
public sealed record MonthActivity(int Year, int Month, IReadOnlyList<DayActivity> Days);

/// <summary>Reads per-record history and corrects a previously recorded result.</summary>
public interface ILearningRecordService
{
    /// <summary>All records made on the given local day, ordered by time.</summary>
    Task<IReadOnlyList<LearningRecordDetail>> GetDayAsync(DateTime localDate, CancellationToken cancellationToken);

    /// <summary>Per-day aggregates for the given local month.</summary>
    Task<MonthActivity> GetMonthAsync(int year, int month, CancellationToken cancellationToken);

    /// <summary>
    /// Changes one record's result and rebuilds the word's review plan from its full history so the
    /// statistics and the review schedule can never disagree.
    /// </summary>
    Task<bool> ChangeResultAsync(int recordId, StudyResult newResult, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class LearningRecordService(
    IDbContextFactory<AppDbContext> contextFactory,
    IReviewScheduler scheduler,
    IClock clock,
    ILogger<LearningRecordService> logger) : ILearningRecordService
{
    public async Task<IReadOnlyList<LearningRecordDetail>> GetDayAsync(
        DateTime localDate,
        CancellationToken cancellationToken)
    {
        var dayStartUtc = clock.ToUtc(localDate.Date);
        var dayEndUtc = clock.ToUtc(localDate.Date.AddDays(1));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.LearningRecords
            .AsNoTracking()
            .Where(r => r.ReviewTime >= dayStartUtc && r.ReviewTime < dayEndUtc)
            .OrderBy(r => r.ReviewTime)
            .ThenBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.WordId,
                r.Result,
                r.ReviewTime,
                WordText = r.Word!.WordText,
                r.Word.Phonetic,
                r.Word.Meaning,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new LearningRecordDetail(
                RecordId: r.Id,
                WordId: r.WordId,
                WordText: r.WordText,
                Phonetic: r.Phonetic,
                Meaning: r.Meaning,
                Result: r.Result,
                ReviewedAtLocal: clock.ToLocal(r.ReviewTime)))
            .ToList();
    }

    public async Task<MonthActivity> GetMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var firstOfMonth = new DateTime(year, month, 1);
        var firstOfNext = firstOfMonth.AddMonths(1);
        var startUtc = clock.ToUtc(firstOfMonth);
        var endUtc = clock.ToUtc(firstOfNext);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.LearningRecords
            .AsNoTracking()
            .Where(r => r.ReviewTime >= startUtc && r.ReviewTime < endUtc)
            .Select(r => new { r.WordId, r.Result, r.ReviewTime })
            .ToListAsync(cancellationToken);

        var days = rows
            .GroupBy(r => clock.ToLocal(r.ReviewTime).Date)
            .Select(g => new DayActivity(
                LocalDate: g.Key,
                StudiedWords: g.Select(r => r.WordId).Distinct().Count(),
                Known: g.Count(r => r.Result == StudyResult.Known),
                Unknown: g.Count(r => r.Result == StudyResult.Unknown),
                Fuzzy: g.Count(r => r.Result == StudyResult.Fuzzy)))
            .OrderBy(d => d.LocalDate)
            .ToList();

        return new MonthActivity(year, month, days);
    }

    public async Task<bool> ChangeResultAsync(
        int recordId,
        StudyResult newResult,
        CancellationToken cancellationToken)
    {
        if (newResult is not (StudyResult.Known or StudyResult.Unknown))
        {
            throw new ArgumentOutOfRangeException(
                nameof(newResult), newResult, "Only Known and Unknown can be applied from the dashboard.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var record = await context.LearningRecords.FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken);
        if (record is null)
        {
            logger.LogWarning("Cannot adjust record {RecordId}: it no longer exists.", recordId);
            return false;
        }

        if (record.Result == newResult)
        {
            return true;
        }

        var wordId = record.WordId;

        // Replay the whole history of this word with the corrected result applied, so the stored snapshots
        // and the authoritative review state are rebuilt from one deterministic pass.
        var history = await context.LearningRecords
            .Where(r => r.WordId == wordId)
            .OrderBy(r => r.ReviewTime)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var algorithm = scheduler.CurrentAlgorithm;
        var retention = scheduler.DesiredRetention;
        var outcome = scheduler.CreateInitial(history[0].ReviewTime, algorithm, retention);

        foreach (var item in history)
        {
            var result = item.Id == recordId ? newResult : item.Result;
            outcome = scheduler.Apply(outcome, result, item.ReviewTime, algorithm, retention);

            item.Result = result;
            item.CorrectCount = outcome.CorrectCount;
            item.WrongCount = outcome.WrongCount;
            item.MasteryLevel = outcome.MasteryLevel;
            item.NextReviewTime = outcome.NextReviewTime;
        }

        var state = await context.WordReviewStates.FirstOrDefaultAsync(s => s.WordId == wordId, cancellationToken);
        if (state is null)
        {
            state = new WordReviewState { WordId = wordId };
            context.WordReviewStates.Add(state);
        }

        state.Stage = outcome.Stage;
        state.CorrectCount = outcome.CorrectCount;
        state.WrongCount = outcome.WrongCount;
        state.MasteryLevel = outcome.MasteryLevel;
        state.NextReviewTime = outcome.NextReviewTime;
        state.LastReviewedAt = outcome.ReviewedAt;
        state.Algorithm = outcome.Algorithm;
        state.AlgorithmVersion = outcome.AlgorithmVersion;
        state.Sm2Repetitions = outcome.Sm2Repetitions;
        state.Sm2EaseFactor = outcome.Sm2EaseFactor;
        state.Sm2IntervalDays = outcome.Sm2IntervalDays;
        state.FsrsStability = outcome.FsrsStability;
        state.FsrsDifficulty = outcome.FsrsDifficulty;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Adjusted record {RecordId} to {Result} for word {WordId}; rebuilt plan stage={Stage} mastery={Mastery}",
            recordId, newResult, wordId, outcome.Stage, outcome.MasteryLevel);

        return true;
    }
}
