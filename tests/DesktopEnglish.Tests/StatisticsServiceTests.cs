using DesktopEnglish.Data;
using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DesktopEnglish.Tests;

/// <summary>Verifies the statistic computations: per-result split, total counts, mastered/due words and streak.</summary>
public class StatisticsServiceTests
{
    private static async Task AddRecordAsync(
        AppDbContext context,
        int wordId,
        DateTime reviewTimeUtc,
        StudyResult result)
    {
        if (!await context.Words.AnyAsync(w => w.Id == wordId))
        {
            context.Words.Add(new Word { Id = wordId, WordText = $"w{wordId}", Level = 1, Category = "test" });
            await context.SaveChangesAsync();
        }

        context.LearningRecords.Add(new LearningRecord
        {
            WordId = wordId,
            ReviewTime = reviewTimeUtc,
            Result = result,
            CorrectCount = result == StudyResult.Unknown ? 0 : 1,
            WrongCount = result == StudyResult.Unknown ? 1 : 0,
            MasteryLevel = 0,
            NextReviewTime = reviewTimeUtc.AddDays(1),
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Empty_database_reports_zeroes()
    {
        await using var db = await TestDatabase.CreateAsync();

        var stats = await new StatisticsService(db.Factory, db.Clock).GetAsync(CancellationToken.None);

        Assert.Equal(0, stats.TodayStudied);
        Assert.Equal(0, stats.TotalRecords);
        Assert.Equal(0, stats.StreakDays);
        Assert.Equal(0, stats.MasteredWords);
        Assert.Equal(0, stats.TotalWords);
    }

    [Fact]
    public async Task Today_counts_are_split_by_result()
    {
        await using var db = await TestDatabase.CreateAsync();
        var baseTime = db.Clock.ToUtc(db.Clock.LocalNow.Date.AddHours(9));

        await AddRecordAsync(db.Context, 1, baseTime, StudyResult.Known);
        await AddRecordAsync(db.Context, 1, baseTime.AddMinutes(5), StudyResult.Known);
        await AddRecordAsync(db.Context, 2, baseTime.AddMinutes(10), StudyResult.Fuzzy);
        await AddRecordAsync(db.Context, 3, baseTime.AddMinutes(15), StudyResult.Unknown);
        // A record from yesterday must not leak into today's counts.
        await AddRecordAsync(db.Context, 4, baseTime.AddDays(-1), StudyResult.Known);

        var stats = await new StatisticsService(db.Factory, db.Clock).GetAsync(CancellationToken.None);

        Assert.Equal(3, stats.TodayStudied);
        Assert.Equal(2, stats.TodayKnown);
        Assert.Equal(1, stats.TodayFuzzy);
        Assert.Equal(1, stats.TodayUnknown);
        Assert.Equal(5, stats.TotalRecords);
        Assert.Equal(2, stats.StreakDays);
    }

    [Fact]
    public async Task A_gap_breaks_the_streak()
    {
        await using var db = await TestDatabase.CreateAsync();
        var today = db.Clock.LocalNow.Date;

        await AddRecordAsync(db.Context, 1, db.Clock.ToUtc(today), StudyResult.Known);
        await AddRecordAsync(db.Context, 2, db.Clock.ToUtc(today.AddDays(-1)), StudyResult.Known);
        await AddRecordAsync(db.Context, 3, db.Clock.ToUtc(today.AddDays(-4)), StudyResult.Known);

        var stats = await new StatisticsService(db.Factory, db.Clock).GetAsync(CancellationToken.None);

        Assert.Equal(2, stats.StreakDays);
    }

    [Fact]
    public async Task An_unstudied_today_does_not_break_an_existing_streak()
    {
        await using var db = await TestDatabase.CreateAsync();
        var today = db.Clock.LocalNow.Date;

        await AddRecordAsync(db.Context, 1, db.Clock.ToUtc(today.AddDays(-1)), StudyResult.Known);
        await AddRecordAsync(db.Context, 2, db.Clock.ToUtc(today.AddDays(-2)), StudyResult.Known);

        var stats = await new StatisticsService(db.Factory, db.Clock).GetAsync(CancellationToken.None);

        Assert.Equal(2, stats.StreakDays);
    }

    [Fact]
    public async Task Mastered_and_due_counts_come_from_the_review_state()
    {
        await using var db = await TestDatabase.CreateAsync();
        var now = db.Clock.UtcNow;

        db.Context.Words.AddRange(Enumerable.Range(1, 3).Select(i => new Word
        {
            Id = i,
            WordText = $"w{i}",
            Level = 1,
            Category = "test",
        }));

        db.Context.WordReviewStates.AddRange(
            new WordReviewState { WordId = 1, Stage = 5, MasteryLevel = 5, NextReviewTime = now.AddDays(3), LastReviewedAt = now },
            new WordReviewState { WordId = 2, Stage = 0, MasteryLevel = 0, NextReviewTime = now.AddHours(-1), LastReviewedAt = now },
            new WordReviewState { WordId = 3, Stage = 0, MasteryLevel = 0, NextReviewTime = now.AddHours(2), LastReviewedAt = now });

        await db.Context.SaveChangesAsync();

        var stats = await new StatisticsService(db.Factory, db.Clock).GetAsync(CancellationToken.None);

        Assert.Equal(1, stats.MasteredWords);
        Assert.Equal(1, stats.DueWords);
        Assert.Equal(3, stats.TotalWords);
    }
}

