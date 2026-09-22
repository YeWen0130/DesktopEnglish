using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopEnglish.Services;

/// <summary>
/// Maps raw imported rows onto the domain shape: derives the difficulty level from the frequency score and
/// extracts parts of speech from the Chinese definition prefix.
/// </summary>
public static partial class WordImportMapping
{
    /// <summary>
    /// Converts the source frequency score into a 1-10 difficulty level.
    /// In this data set a higher score means a more obscure word, so 1 is the most common word.
    /// Each source row is validated for its keys before being mapped, so malformed values are skipped
    /// rather than imported with a nonsense level.
    /// </summary>
    public static bool TryMapLevel(string? rawFrequency, out int level)
    {
        level = 1;

        if (string.IsNullOrWhiteSpace(rawFrequency))
        {
            return true;
        }

        if (!double.TryParse(rawFrequency, NumberStyles.Float, CultureInfo.InvariantCulture, out var frequency))
        {
            return false;
        }

        level = Math.Clamp((int)Math.Round(frequency, MidpointRounding.AwayFromZero), 1, 10);
        return true;
    }

    /// <summary>
    /// Extracts the distinct parts of speech that prefix a Chinese definition, e.g.
    /// <c>"n. 协议;  vt. 授予;  v. (与...)一致"</c> becomes <c>"n. / vt. / v."</c>.
    /// Order of first appearance is preserved.
    /// </summary>
    public static string ExtractPartOfSpeech(string? meaning)
    {
        if (string.IsNullOrWhiteSpace(meaning))
        {
            return string.Empty;
        }

        var found = new List<string>();
        foreach (Match match in PartOfSpeechPattern().Matches(meaning))
        {
            // The regex captures only the letters, so append the period to keep the familiar "n." notation.
            var token = $"{match.Groups["pos"].Value.Trim()}.";

            if (!found.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                found.Add(token);
            }
        }

        return string.Join(" / ", found);
    }

    public static string NormalizeWord(string? word) => (word ?? string.Empty).Trim();

    /// <summary>Recognizes the abbreviation tokens used as definition prefixes in the source data.</summary>
    [GeneratedRegex(
        @"(?<![A-Za-z])(?<pos>abbr|adj|adv|conj|int|n|num|prep|pron|vt|vi|v)\s*\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartOfSpeechPattern();
}


