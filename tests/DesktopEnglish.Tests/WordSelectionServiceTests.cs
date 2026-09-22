using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DesktopEnglish.Tests;

/// <summary>
/// Verifies that due reviews come first, new words fill the remainder, and the daily limit is a hard cap.
/// </summary>
public class WordSelectionServiceTests
{
    private static async Task<TestDatabase> CreateSeededAsync()
    {
        var db = await TestDatabase.CreateAsync();

        db.Context.Words.AddRange(
            new Word { Id = 1, WordText = "apple", Level = 1, Category = "百词斩" },
            new Word { Id = 2, WordText = "banana", Level = 3, Category = "百词斩" },
            new Word { Id = 3, WordText = "cherry", Level = 2, Category = "百词斩" },
            new Word { Id = 4, WordText = "durian", Level = 9, Category = "百词斩" });

        // Words 1 and 2 have been studied; word 1 is overdue, word 2 is still in the future.
        db.Context.WordReviewStates.Add(new WordReviewState
        {
            WordId = 1,
            Stage = 1,
            MasteryLevel = 1,
            NextReviewTime = db.Clock.UtcNow.AddHours(-2),
            LastReviewedAt = db.Clock.UtcNow.AddDays(-2),
        });

        db.Context.WordReviewStates.Add(new WordReviewState
        {
            WordId = 2,
            Stage = 1,
            MasteryLevel = 1,
            NextReviewTime = db.Clock.UtcNow.AddDays(2),
            LastReviewedAt = db.Clock.UtcNow.AddDays(-1),
        });

        await db.Context.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Due_reviews_are_returned_first()
    {
        await using var db = await CreateSeededAsync();
        var selection = new WordSelectionService(db.Factory, db.Clock);

        var due = await selection.SelectDueReviewsAsync(10, CancellationToken.None);

        Assert.Single(due);
        Assert.Equal("apple", due[0].WordText);
    }

    [Fact]
    public async Task New_words_are_ordered_by_level()
    {
        await using var db = await CreateSeededAsync();
        var selection = new WordSelectionService(db.Factory, db.Clock);

        var fresh = await selection.SelectNewWordsAsync(10, CancellationToken.None);

        // Words 3 and 4 have never been studied; easier (lower) level comes first.
        Assert.Equal(2, fresh.Count);
        Assert.Equal("cherry", fresh[0].WordText);
        Assert.Equal("durian", fresh[1].WordText);
    }

    [Fact]
    public async Task Daily_quota_counts_distinct_words_studied_today()
    {
        await using var db = await CreateSeededAsync();

        var today = db.Clock.ToUtc(db.Clock.LocalNow.Date.AddHours(9));
        db.Context.LearningRecords.Add(new LearningRecord { WordId = 1, ReviewTime = today, Result = StudyResult.Known });
        db.Context.LearningRecords.Add(new LearningRecord { WordId = 2, ReviewTime = today.AddMinutes(1), Result = StudyResult.Known });
        db.Context.LearningRecords.Add(new LearningRecord { WordId = 1, ReviewTime = today.AddMinutes(2), Result = StudyResult.Fuzzy });
        await db.Context.SaveChangesAsync();

        var selection = new WordSelectionService(db.Factory, db.Clock);

        Assert.Equal(2, await selection.GetStudiedTodayCountAsync(CancellationToken.None));
        Assert.Equal(3, await selection.GetRemainingDailyQuotaAsync(5, CancellationToken.None));
        Assert.Equal(0, await selection.GetRemainingDailyQuotaAsync(2, CancellationToken.None));
    }

    [Fact]
    public async Task Zero_limit_returns_no_words()
    {
        await using var db = await CreateSeededAsync();
        var selection = new WordSelectionService(db.Factory, db.Clock);

        Assert.Empty(await selection.SelectDueReviewsAsync(0, CancellationToken.None));
        Assert.Empty(await selection.SelectNewWordsAsync(0, CancellationToken.None));
    }
}





