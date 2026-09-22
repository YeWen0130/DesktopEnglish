using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>
/// Imports words from CSV or JSON into SQLite.
/// Parsing is separated from persistence so the mapping rules can be tested without a database.
/// </summary>
public sealed class WordImportService(
    IDbContextFactory<AppDbContext> contextFactory,
    IExampleProvider exampleProvider,
    IClock clock,
    ILogger<WordImportService> logger) : IWordImportService
{
    public async Task<IReadOnlyList<ImportedWord>> ParseAsync(
        Stream stream,
        ImportFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return format switch
        {
            ImportFormat.Csv => await ParseCsvAsync(stream, cancellationToken),
            ImportFormat.Json => await ParseJsonAsync(stream, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported import format."),
        };
    }

    public async Task<ImportResult> ImportAsync(
        Stream stream,
        ImportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        var parsed = await ParseAsync(stream, options.Format, cancellationToken);
        return await PersistAsync(parsed, options, cancellationToken);
    }

    private async Task<IReadOnlyList<ImportedWord>> ParseCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        var results = new List<ImportedWord>();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // The source file starts with a UTF-8 BOM ("\ufefftopic_id"), so detect and strip it.
            DetectDelimiter = false,
            Delimiter = ",",
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim,
        };

        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync().ConfigureAwait(false))
        {
            return results;
        }

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];

        var wordColumn = FindColumn(headers, "单词", "word");
        var phoneticColumn = FindColumn(headers, "音标", "phonetic");
        var meaningColumn = FindColumn(headers, "中文释义", "meaning", "释义");
        var frequencyColumn = FindColumn(headers, "词频", "frequency", "freq", "level");

        if (wordColumn is null)
        {
            throw new InvalidDataException("导入失败：CSV 缺少“单词”列。");
        }

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var word = WordImportMapping.NormalizeWord(csv.GetField(wordColumn));
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            var meaning = meaningColumn is null ? string.Empty : csv.GetField(meaningColumn) ?? string.Empty;

            if (!WordImportMapping.TryMapLevel(
                    frequencyColumn is null ? null : csv.GetField(frequencyColumn),
                    out var level))
            {
                continue;
            }

            results.Add(new ImportedWord(
                Word: word,
                Phonetic: phoneticColumn is null ? string.Empty : csv.GetField(phoneticColumn) ?? string.Empty,
                Meaning: meaning,
                PartOfSpeech: WordImportMapping.ExtractPartOfSpeech(meaning),
                Example: string.Empty,
                Level: level));
        }

        return results;
    }

    private async Task<IReadOnlyList<ImportedWord>> ParseJsonAsync(Stream stream, CancellationToken cancellationToken)
    {
        var payload = await JsonSerializer.DeserializeAsync<List<JsonWordDto>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken) ?? [];

        var results = new List<ImportedWord>(payload.Count);

        foreach (var dto in payload)
        {
            var word = WordImportMapping.NormalizeWord(dto.Word);
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            var meaning = dto.Meaning ?? string.Empty;

            results.Add(new ImportedWord(
                Word: word,
                Phonetic: dto.Phonetic ?? string.Empty,
                Meaning: meaning,
                PartOfSpeech: string.IsNullOrWhiteSpace(dto.PartOfSpeech)
                    ? WordImportMapping.ExtractPartOfSpeech(meaning)
                    : dto.PartOfSpeech!,
                Example: dto.Example ?? string.Empty,
                Level: Math.Clamp(dto.Level <= 0 ? 1 : dto.Level, 1, 10)));
        }

        return results;
    }

    private async Task<ImportResult> PersistAsync(
        IReadOnlyList<ImportedWord> parsed,
        ImportOptions options,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var updated = 0;
        var skipped = 0;
        var invalid = 0;
        var errors = new List<string>();
        var batchSize = Math.Max(1, options.BatchSize);
        var nowUtc = clock.UtcNow;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // One read of the existing keys keeps the per-row duplicate check free of N+1 queries.
        var existing = await context.Words
            .ToDictionaryAsync(w => w.WordText, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = 0;

        foreach (var item in parsed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!seenInFile.Add(item.Word))
            {
                invalid++;
                continue;
            }

            var example = item.Example;
            if (string.IsNullOrWhiteSpace(example))
            {
                example = await exampleProvider.GetExampleAsync(item.Word, cancellationToken) ?? string.Empty;
            }

            if (existing.TryGetValue(item.Word, out var current))
            {
                if (options.Duplicates == DuplicatePolicy.Skip)
                {
                    skipped++;
                    continue;
                }

                current.Phonetic = item.Phonetic;
                current.Meaning = item.Meaning;
                current.PartOfSpeech = item.PartOfSpeech;
                current.Level = item.Level;
                current.Category = options.Category;
                current.UpdatedAt = nowUtc;
                if (!string.IsNullOrWhiteSpace(example))
                {
                    current.Example = example;
                }

                updated++;
            }
            else
            {
                var entity = new Word
                {
                    WordText = item.Word,
                    Phonetic = item.Phonetic,
                    Meaning = item.Meaning,
                    PartOfSpeech = item.PartOfSpeech,
                    Example = example,
                    Level = item.Level,
                    Category = options.Category,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                };

                context.Words.Add(entity);
                existing[item.Word] = entity;
                added++;
            }

            pending++;
            if (pending >= batchSize)
            {
                await context.SaveChangesAsync(cancellationToken);
                pending = 0;
            }
        }

        if (pending > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Word import finished: added={Added} updated={Updated} skipped={Skipped} invalid={Invalid}",
            added, updated, skipped, invalid);

        return new ImportResult(added, updated, skipped, invalid, errors);
    }

    private static string? FindColumn(string[] headers, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var match = headers.FirstOrDefault(h =>
                !string.IsNullOrWhiteSpace(h) &&
                h.Trim('\ufeff', '"', ' ').Equals(candidate, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private sealed class JsonWordDto
    {
        public string? Word { get; set; }

        public string? Phonetic { get; set; }

        public string? Meaning { get; set; }

        public string? PartOfSpeech { get; set; }

        public string? Example { get; set; }

        public int Level { get; set; }
    }
}




