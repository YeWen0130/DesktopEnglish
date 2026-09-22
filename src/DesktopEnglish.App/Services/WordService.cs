using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using Microsoft.EntityFrameworkCore;

namespace DesktopEnglish.Services;

/// <summary>CRUD and query operations over the dictionary. All database access lives here, never in the UI.</summary>
public interface IWordService
{
    Task<WordPage> QueryAsync(WordQuery query, CancellationToken cancellationToken);

    Task<Word?> GetAsync(int id, CancellationToken cancellationToken);

    Task<Word> AddAsync(Word word, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(Word word, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);
}

public sealed record WordQuery(
    string? Search = null,
    string? Category = null,
    int? Level = null,
    int Page = 1,
    int PageSize = 50);

public sealed record WordPage(IReadOnlyList<Word> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

/// <inheritdoc />
public sealed class WordService(
    IDbContextFactory<AppDbContext> contextFactory,
    IClock clock) : IWordService
{
    public async Task<WordPage> QueryAsync(WordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 500);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var words = context.Words.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            words = words.Where(w =>
                EF.Functions.Like(w.WordText, $"%{term}%") ||
                EF.Functions.Like(w.Meaning, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            words = words.Where(w => w.Category == query.Category);
        }

        if (query.Level is int level)
        {
            words = words.Where(w => w.Level == level);
        }

        var total = await words.CountAsync(cancellationToken);

        var items = await words
            .OrderBy(w => w.WordText)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new WordPage(items, total, page, pageSize);
    }

    public async Task<Word?> GetAsync(int id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Words.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<Word> AddAsync(Word word, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(word);

        var nowUtc = clock.UtcNow;
        word.CreatedAt = nowUtc;
        word.UpdatedAt = nowUtc;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Words.Add(word);
        await context.SaveChangesAsync(cancellationToken);

        return word;
    }

    public async Task<bool> UpdateAsync(Word word, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(word);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.Words.FirstOrDefaultAsync(w => w.Id == word.Id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        existing.WordText = word.WordText;
        existing.Phonetic = word.Phonetic;
        existing.Meaning = word.Meaning;
        existing.PartOfSpeech = word.PartOfSpeech;
        existing.Example = word.Example;
        existing.Level = word.Level;
        existing.Category = word.Category;
        existing.UpdatedAt = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.Words.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        // Review state and records cascade with the word.
        context.Words.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Words
            .Where(w => w.Category != string.Empty)
            .Select(w => w.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);
    }
}
