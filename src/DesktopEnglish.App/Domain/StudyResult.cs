namespace DesktopEnglish.Domain;

/// <summary>
/// Outcome the user selected for a single word during a study round.
/// </summary>
public enum StudyResult
{
    /// <summary>The user knew the word.</summary>
    Known = 0,

    /// <summary>The user recalled it only partially / with effort.</summary>
    Fuzzy = 1,

    /// <summary>The user did not know the word.</summary>
    Unknown = 2,
}
