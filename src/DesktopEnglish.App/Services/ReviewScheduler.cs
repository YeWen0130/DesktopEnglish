using DesktopEnglish.Domain;

namespace DesktopEnglish.Services;

/// <summary>
/// Simple, deterministic interval-repetition scheduler.
/// The interval table is indexed by the review stage: a known answer advances one stage, an unknown answer
/// steps back one stage, and a fuzzy answer holds the stage while keeping the word on the current interval.
/// </summary>
public sealed class ReviewScheduler : IReviewScheduler
{
    /// <summary>Interval in days for each stage. Index 0 is the first review after meeting a new word.</summary>
    private static readonly int[] IntervalDays = [1, 2, 4, 7, 15, 30, 60];

    public int MaxStage => IntervalDays.Length - 1;

    public int MasteredLevel => 5;

    public ReviewOutcome CreateInitial(DateTime nowUtc) =>
        new(Stage: 0, CorrectCount: 0, WrongCount: 0, MasteryLevel: 0, NextReviewTime: nowUtc, ReviewedAt: nowUtc);

    public ReviewOutcome Apply(ReviewOutcome current, StudyResult result, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(current);

        var stage = current.Stage;
        var correct = current.CorrectCount;
        var wrong = current.WrongCount;

        switch (result)
        {
            case StudyResult.Known:
                correct++;
                stage = Math.Min(stage + 1, MaxStage);
                break;

            case StudyResult.Fuzzy:
                // Recalled with effort: hold the current interval, but still count it as a success.
                correct++;
                break;

            case StudyResult.Unknown:
                wrong++;
                stage = Math.Max(stage - 1, 0);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported study result.");
        }

        return new ReviewOutcome(
            Stage: stage,
            CorrectCount: correct,
            WrongCount: wrong,
            MasteryLevel: Math.Min(stage, MasteredLevel),
            NextReviewTime: nowUtc.AddDays(IntervalDays[stage]),
            ReviewedAt: nowUtc);
    }
}
