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
    DateTime ReviewedAt);

/// <summary>
/// Computes the next review state for a word from the user's answer.
/// Pure and synchronous so it stays trivially testable and swappable for SM-2 later.
/// </summary>
public interface IReviewScheduler
{
    /// <summary>Highest stage index, i.e. the last entry of the interval table.</summary>
    int MaxStage { get; }

    /// <summary>Mastery level that marks a word as mastered.</summary>
    int MasteredLevel { get; }

    /// <summary>Initial state for a word that has never been studied: due immediately.</summary>
    ReviewOutcome CreateInitial(DateTime nowUtc);

    /// <summary>Applies an answer to the current state and returns the resulting state.</summary>
    ReviewOutcome Apply(ReviewOutcome current, StudyResult result, DateTime nowUtc);
}
