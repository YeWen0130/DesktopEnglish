using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>Records an answer and returns the resulting review state.</summary>
public interface IStudySessionService
{
    Task<ReviewOutcome> RecordResultAsync(int wordId, StudyResult result, CancellationToken cancellationToken);
}

/// <summary>
/// Applies a study answer: updates the authoritative review state and appends a study record in one
/// transaction so statistics and the review plan can never disagree.
/// </summary>
public sealed class StudySessionService(
    IDbContextFactory<AppDbContext> contextFactory,
    IReviewScheduler scheduler,
    IClock clock,
    ILogger<StudySessionService> logger) : IStudySessionService
{
    public async Task<ReviewOutcome> RecordResultAsync(
        int wordId,
        StudyResult result,
        CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var state = await context.WordReviewStates
            .FirstOrDefaultAsync(s => s.WordId == wordId, cancellationToken);

        ReviewOutcome current;
        if (state is null || state.Algorithm != scheduler.CurrentAlgorithm)
        {
            var history = await context.LearningRecords
                .AsNoTracking()
                .Where(r => r.WordId == wordId)
                .OrderBy(r => r.ReviewTime)
                .ThenBy(r => r.Id)
                .ToListAsync(cancellationToken);

            current = scheduler.CreateInitial(
                history.Count == 0 ? nowUtc : history[0].ReviewTime,
                scheduler.CurrentAlgorithm,
                scheduler.DesiredRetention);

            foreach (var record in history)
            {
                current = scheduler.Apply(current, record.Result, record.ReviewTime,
                    scheduler.CurrentAlgorithm, scheduler.DesiredRetention);
            }
        }
        else
        {
            current = new ReviewOutcome(
                Stage: state.Stage,
                CorrectCount: state.CorrectCount,
                WrongCount: state.WrongCount,
                MasteryLevel: state.MasteryLevel,
                NextReviewTime: state.NextReviewTime,
                ReviewedAt: state.LastReviewedAt,
                Algorithm: state.Algorithm,
                AlgorithmVersion: state.AlgorithmVersion,
                Sm2Repetitions: state.Sm2Repetitions,
                Sm2EaseFactor: state.Sm2EaseFactor,
                Sm2IntervalDays: state.Sm2IntervalDays,
                FsrsStability: state.FsrsStability,
                FsrsDifficulty: state.FsrsDifficulty);
        }

        var outcome = scheduler.Apply(current, result, nowUtc,
            scheduler.CurrentAlgorithm, scheduler.DesiredRetention);

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

        context.LearningRecords.Add(new LearningRecord
        {
            WordId = wordId,
            ReviewTime = outcome.ReviewedAt,
            Result = result,
            CorrectCount = outcome.CorrectCount,
            WrongCount = outcome.WrongCount,
            MasteryLevel = outcome.MasteryLevel,
            NextReviewTime = outcome.NextReviewTime,
            Algorithm = outcome.Algorithm,
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Recorded {Result} for word {WordId}: stage={Stage} mastery={Mastery} next={Next:u}",
            result, wordId, outcome.Stage, outcome.MasteryLevel, outcome.NextReviewTime);

        return outcome;
    }
}
