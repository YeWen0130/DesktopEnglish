using DesktopEnglish.Domain;

namespace DesktopEnglish.Services;

/// <summary>
/// Immutable result of applying a <see cref="StudyResult"/> to a word's review state.
/// Deliberately independent of the EF entity so the algorithm is testable without a database.
/// </summary>
public sealed record ReviewOutcome(
    int Stage,
    int CorrectCount,
    int WrongCount,
    int MasteryLevel,
    DateTime NextReviewTime,
    DateTime ReviewedAt,
    ReviewAlgorithm Algorithm = ReviewAlgorithm.Classic,
    int AlgorithmVersion = 1,
    int Sm2Repetitions = 0,
    double Sm2EaseFactor = 2.5,
    double Sm2IntervalDays = 0,
    double FsrsStability = 0,
    double FsrsDifficulty = 0);

/// <summary>
/// Computes the next review state for a word from the user's answer.
/// Pure and synchronous so it stays trivially testable and swappable for SM-2 later.
/// </summary>
public interface IReviewScheduler
{
    ReviewAlgorithm CurrentAlgorithm => ReviewAlgorithm.Classic;

    double DesiredRetention => 0.9;

    /// <summary>Highest stage index, i.e. the last entry of the interval table.</summary>
    int MaxStage { get; }

    /// <summary>Mastery level that marks a word as mastered.</summary>
    int MasteredLevel { get; }

    /// <summary>Initial state for a word that has never been studied: due immediately.</summary>
    ReviewOutcome CreateInitial(DateTime nowUtc);

    /// <summary>Applies an answer to the current state and returns the resulting state.</summary>
    ReviewOutcome Apply(ReviewOutcome current, StudyResult result, DateTime nowUtc);

    ReviewOutcome CreateInitial(DateTime nowUtc, ReviewAlgorithm algorithm, double desiredRetention) =>
        CreateInitial(nowUtc);

    ReviewOutcome Apply(ReviewOutcome current, StudyResult result, DateTime nowUtc,
        ReviewAlgorithm algorithm, double desiredRetention) => Apply(current, result, nowUtc);
}
