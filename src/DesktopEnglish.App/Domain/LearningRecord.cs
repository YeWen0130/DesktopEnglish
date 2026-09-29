namespace DesktopEnglish.Domain;

/// <summary>
/// Append-only log of every study interaction. One row per answered word per round.
/// </summary>
public class LearningRecord
{
    public int Id { get; set; }

    public int WordId { get; set; }

    public Word? Word { get; set; }

    /// <summary>Timestamp (UTC) at which the result was recorded.</summary>
    public DateTime ReviewTime { get; set; }

    public StudyResult Result { get; set; }

    /// <summary>Running correct count for the word after this result was applied.</summary>
    public int CorrectCount { get; set; }

    /// <summary>Running wrong count for the word after this result was applied.</summary>
    public int WrongCount { get; set; }

    /// <summary>Mastery level (0-5) after this result was applied.</summary>
    public int MasteryLevel { get; set; }

    /// <summary>Review time (UTC) computed for the word after this result was applied.</summary>
    public DateTime NextReviewTime { get; set; }

    public ReviewAlgorithm Algorithm { get; set; } = ReviewAlgorithm.Classic;
}
