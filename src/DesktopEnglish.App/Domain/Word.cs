namespace DesktopEnglish.Domain;

/// <summary>
/// A vocabulary entry managed by the application.
/// </summary>
public class Word
{
    public int Id { get; set; }

    /// <summary>The English word itself. Unique across the dictionary.</summary>
    public string WordText { get; set; } = string.Empty;

    /// <summary>Phonetic transcription, e.g. <c>/əˈbændənd/</c>.</summary>
    public string Phonetic { get; set; } = string.Empty;

    /// <summary>Chinese definition. Kept verbatim from the imported source.</summary>
    public string Meaning { get; set; } = string.Empty;

    /// <summary>Parsed part(s) of speech, e.g. <c>n. / v.</c>.</summary>
    public string PartOfSpeech { get; set; } = string.Empty;

    /// <summary>English example sentence. Empty until supplied by an example provider or edited by hand.</summary>
    public string Example { get; set; } = string.Empty;

    /// <summary>Difficulty level, 1 (most common) through 10 (most obscure).</summary>
    public int Level { get; set; } = 1;

    /// <summary>Vocabulary book / category the word belongs to.</summary>
    public string Category { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
