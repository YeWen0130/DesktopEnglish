using System.IO;
namespace DesktopEnglish.Services;

public enum ImportFormat
{
    Csv,
    Json,
}

public enum DuplicatePolicy
{
    /// <summary>Keep the existing word untouched.</summary>
    Skip,

    /// <summary>Overwrite the existing word's fields with the imported values.</summary>
    Overwrite,
}

/// <summary>
/// Controls an import run. <paramref name="BatchSize"/> bounds how many rows are flushed per
/// <c>SaveChanges</c> call so a large file does not hold one huge change tracker.
/// </summary>
public sealed record ImportOptions(
    ImportFormat Format,
    DuplicatePolicy Duplicates,
    string Category,
    int BatchSize = 500);

public sealed record ImportResult(
    int Added,
    int Updated,
    int Skipped,
    int Invalid,
    IReadOnlyList<string> Errors)
{
    public int TotalProcessed => Added + Updated + Skipped + Invalid;
}

/// <summary>Imports vocabulary from CSV or JSON without touching the UI.</summary>
public interface IWordImportService
{
    Task<ImportResult> ImportAsync(Stream stream, ImportOptions options, CancellationToken cancellationToken);

    /// <summary>Parses a stream into words without persisting; used for previews and tests.</summary>
    Task<IReadOnlyList<ImportedWord>> ParseAsync(Stream stream, ImportFormat format, CancellationToken cancellationToken);
}

/// <summary>A normalized word parsed from an external file, before it reaches the database.</summary>
public sealed record ImportedWord(
    string Word,
    string Phonetic,
    string Meaning,
    string PartOfSpeech,
    string Example,
    int Level);

