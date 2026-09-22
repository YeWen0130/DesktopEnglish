namespace DesktopEnglish.Domain;

/// <summary>
/// Authoritative current review state (the "review plan") for a word that has been studied at least once.
/// </summary>
public class WordReviewState
{
    /// <summary>Primary key and foreign key to <see cref="Word"/>.</summary>
    public int WordId { get; set; }

    public Word? Word { get; set; }

    /// <summary>Interval table index, 0 through <see cref="ReviewScheduler.MaxStage"/>.</summary>
    public int Stage { get; set; }

    public int CorrectCount { get; set; }

    public int WrongCount { get; set; }

    /// <summary>Mastery level, 0 through 5. Level 5 means the word is considered mastered.</summary>
    public int MasteryLevel { get; set; }

    /// <summary>When the word becomes due again (UTC).</summary>
    public DateTime NextReviewTime { get; set; }

    /// <summary>When the word was last answered (UTC).</summary>
    public DateTime LastReviewedAt { get; set; }
}
