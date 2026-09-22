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

        var current = state is null
            ? scheduler.CreateInitial(nowUtc)
            : new ReviewOutcome(
                Stage: state.Stage,
                CorrectCount: state.CorrectCount,
                WrongCount: state.WrongCount,
                MasteryLevel: state.MasteryLevel,
                NextReviewTime: state.NextReviewTime,
                ReviewedAt: state.LastReviewedAt);

        var outcome = scheduler.Apply(current, result, nowUtc);

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

        context.LearningRecords.Add(new LearningRecord
        {
            WordId = wordId,
            ReviewTime = outcome.ReviewedAt,
            Result = result,
            CorrectCount = outcome.CorrectCount,
            WrongCount = outcome.WrongCount,
            MasteryLevel = outcome.MasteryLevel,
            NextReviewTime = outcome.NextReviewTime,
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Recorded {Result} for word {WordId}: stage={Stage} mastery={Mastery} next={Next:u}",
            result, wordId, outcome.Stage, outcome.MasteryLevel, outcome.NextReviewTime);

        return outcome;
    }
}
