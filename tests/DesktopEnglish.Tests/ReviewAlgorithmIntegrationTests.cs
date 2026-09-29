using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DesktopEnglish.Tests;

public sealed class ReviewAlgorithmIntegrationTests
{
    [Fact]
    public async Task Switching_algorithm_replays_history_without_adding_records()
    {
        await using var db = await TestDatabase.CreateAsync();
        db.Context.Words.Add(new Word { Id = 1, WordText = "switch", Level = 1, Category = "test" });
        await db.Context.SaveChangesAsync();

        var settings = new MutableSettings();
        var scheduler = new ConfiguredReviewScheduler(settings);
        var service = new StudySessionService(db.Factory, scheduler, db.Clock, new NoOpLogger<StudySessionService>());

        await service.RecordResultAsync(1, StudyResult.Known, CancellationToken.None);
        db.Clock.SetUtc(db.Clock.UtcNow.AddDays(1));
        await service.RecordResultAsync(1, StudyResult.Fuzzy, CancellationToken.None);

        settings.Algorithm = ReviewAlgorithm.Fsrs;
        db.Clock.SetUtc(db.Clock.UtcNow.AddDays(1));
        var outcome = await service.RecordResultAsync(1, StudyResult.Known, CancellationToken.None);

        await using var verify = await db.Factory.CreateDbContextAsync();
        var records = await verify.LearningRecords.OrderBy(r => r.Id).ToListAsync();
        var state = await verify.WordReviewStates.SingleAsync();

        Assert.Equal(3, records.Count);
        Assert.All(records.Take(2), record => Assert.Equal(ReviewAlgorithm.Classic, record.Algorithm));
        Assert.Equal(ReviewAlgorithm.Fsrs, records[2].Algorithm);
        Assert.Equal(ReviewAlgorithm.Fsrs, state.Algorithm);
        Assert.Equal(6, state.AlgorithmVersion);
        Assert.Equal(3, outcome.CorrectCount);
    }

    [Fact]
    public async Task Settings_round_trip_algorithm_and_retention()
    {
        await using var db = await TestDatabase.CreateAsync();
        var options = new AppOptions();
        var service = new SettingsService(db.Factory, options, new NoOpLogger<SettingsService>());
        await service.LoadAsync(CancellationToken.None);

        await service.UpdateAsync(settings =>
        {
            settings.ReviewAlgorithm = ReviewAlgorithm.Fsrs;
            settings.FsrsDesiredRetention = 0.95;
            return settings;
        }, CancellationToken.None);

        var reloaded = new SettingsService(db.Factory, options, new NoOpLogger<SettingsService>());
        await reloaded.LoadAsync(CancellationToken.None);

        Assert.Equal(ReviewAlgorithm.Fsrs, reloaded.Current.ReviewAlgorithm);
        Assert.Equal(0.95, reloaded.Current.FsrsDesiredRetention, 6);
    }

    [Fact]
    public async Task Missing_review_state_replays_existing_history_before_applying_the_new_answer()
    {
        await using var db = await TestDatabase.CreateAsync();
        db.Context.Words.Add(new Word { Id = 1, WordText = "orphan", Level = 1, Category = "test" });
        db.Context.LearningRecords.AddRange(
            new LearningRecord
            {
                WordId = 1,
                ReviewTime = db.Clock.UtcNow,
                Result = StudyResult.Known,
                CorrectCount = 1,
                MasteryLevel = 1,
                NextReviewTime = db.Clock.UtcNow.AddDays(1),
                Algorithm = ReviewAlgorithm.Classic,
            },
            new LearningRecord
            {
                WordId = 1,
                ReviewTime = db.Clock.UtcNow.AddDays(1),
                Result = StudyResult.Known,
                CorrectCount = 2,
                MasteryLevel = 2,
                NextReviewTime = db.Clock.UtcNow.AddDays(3),
                Algorithm = ReviewAlgorithm.Classic,
            });
        await db.Context.SaveChangesAsync();

        var settings = new MutableSettings { Algorithm = ReviewAlgorithm.Sm2 };
        var scheduler = new ConfiguredReviewScheduler(settings);
        var service = new StudySessionService(db.Factory, scheduler, db.Clock, new NoOpLogger<StudySessionService>());
        db.Clock.SetUtc(db.Clock.UtcNow.AddDays(2));

        var outcome = await service.RecordResultAsync(1, StudyResult.Known, CancellationToken.None);

        await using var verify = await db.Factory.CreateDbContextAsync();
        var records = await verify.LearningRecords.OrderBy(r => r.Id).ToListAsync();
        var state = await verify.WordReviewStates.SingleAsync();

        Assert.Equal(3, records.Count);
        Assert.Equal(3, outcome.CorrectCount);
        Assert.Equal(3, outcome.Sm2Repetitions);
        Assert.Equal(ReviewAlgorithm.Sm2, state.Algorithm);
    }

    [Fact]
    public async Task Saving_a_new_algorithm_does_not_change_existing_due_dates()
    {
        await using var db = await TestDatabase.CreateAsync();
        db.Context.Words.Add(new Word { Id = 1, WordText = "due", Level = 1, Category = "test" });
        db.Context.WordReviewStates.Add(new WordReviewState
        {
            WordId = 1,
            Stage = 3,
            CorrectCount = 3,
            MasteryLevel = 3,
            NextReviewTime = db.Clock.UtcNow.AddDays(5),
            LastReviewedAt = db.Clock.UtcNow,
            Algorithm = ReviewAlgorithm.Classic,
        });
        await db.Context.SaveChangesAsync();

        var dueBefore = db.Context.WordReviewStates.Local.Single().NextReviewTime;
        var options = new AppOptions();
        var settingsService = new SettingsService(db.Factory, options, new NoOpLogger<SettingsService>());
        await settingsService.LoadAsync(CancellationToken.None);
        await settingsService.UpdateAsync(settings =>
        {
            settings.ReviewAlgorithm = ReviewAlgorithm.Fsrs;
            return settings;
        }, CancellationToken.None);

        await using var verify = await db.Factory.CreateDbContextAsync();
        var state = await verify.WordReviewStates.SingleAsync();

        Assert.Equal(dueBefore, state.NextReviewTime);
        Assert.Equal(ReviewAlgorithm.Classic, state.Algorithm);
    }

    [Fact]
    public async Task Switching_away_and_back_rebuilds_the_same_current_algorithm_state()
    {
        await using var db = await TestDatabase.CreateAsync();
        db.Context.Words.Add(new Word { Id = 1, WordText = "roundtrip", Level = 1, Category = "test" });
        await db.Context.SaveChangesAsync();

        var settings = new MutableSettings();
        var scheduler = new ConfiguredReviewScheduler(settings);
        var service = new StudySessionService(db.Factory, scheduler, db.Clock, new NoOpLogger<StudySessionService>());

        await service.RecordResultAsync(1, StudyResult.Known, CancellationToken.None);
        db.Clock.SetUtc(db.Clock.UtcNow.AddDays(1));
        await service.RecordResultAsync(1, StudyResult.Fuzzy, CancellationToken.None);

        settings.Algorithm = ReviewAlgorithm.Fsrs;
        db.Clock.SetUtc(db.Clock.UtcNow.AddDays(1));
        await service.RecordResultAsync(1, StudyResult.Known, CancellationToken.None);

        await using (var firstFsrs = await db.Factory.CreateDbContextAsync())
        {
            var state = await firstFsrs.WordReviewStates.SingleAsync();
            Assert.Equal(ReviewAlgorithm.Fsrs, state.Algorithm);
            Assert.True(state.FsrsStability > 0);
        }

        settings.Algorithm = ReviewAlgorithm.Classic;
        db.Clock.SetUtc(db.Clock.UtcNow.AddDays(1));
        await service.RecordResultAsync(1, StudyResult.Known, CancellationToken.None);

        settings.Algorithm = ReviewAlgorithm.Fsrs;
        db.Clock.SetUtc(db.Clock.UtcNow.AddDays(1));
        var outcome = await service.RecordResultAsync(1, StudyResult.Known, CancellationToken.None);

        await using var verify = await db.Factory.CreateDbContextAsync();
        var finalState = await verify.WordReviewStates.SingleAsync();
        var records = await verify.LearningRecords.CountAsync();

        Assert.Equal(ReviewAlgorithm.Fsrs, outcome.Algorithm);
        Assert.Equal(ReviewAlgorithm.Fsrs, finalState.Algorithm);
        Assert.Equal(5, records);
        Assert.Equal(5, finalState.CorrectCount);
        Assert.True(finalState.FsrsStability > 0);
    }

    [Fact]
    public async Task Failed_study_write_rolls_back_both_the_record_and_the_review_state()
    {
        await using var db = await TestDatabase.CreateAsync();
        db.Context.Words.Add(new Word { Id = 1, WordText = "rollback", Level = 1, Category = "test" });
        await db.Context.SaveChangesAsync();
        await db.Context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER FailLearningRecordInsert BEFORE INSERT ON LearningRecords " +
            "BEGIN SELECT RAISE(ABORT, 'simulated write failure'); END;");

        var settings = new MutableSettings { Algorithm = ReviewAlgorithm.Fsrs };
        var scheduler = new ConfiguredReviewScheduler(settings);
        var service = new StudySessionService(db.Factory, scheduler, db.Clock, new NoOpLogger<StudySessionService>());

        await Assert.ThrowsAsync<DbUpdateException>(
            () => service.RecordResultAsync(1, StudyResult.Known, CancellationToken.None));

        await using var verify = await db.Factory.CreateDbContextAsync();
        Assert.Empty(await verify.LearningRecords.ToListAsync());
        Assert.Empty(await verify.WordReviewStates.ToListAsync());
    }

    private sealed class MutableSettings : ISettingsService
    {
        public ReviewAlgorithm Algorithm { get; set; } = ReviewAlgorithm.Classic;

        public UserSettingsSnapshot Current => new(
            30, 20, false, true, true, 1, Algorithm, 0.9);

        public event EventHandler<UserSettingsSnapshot>? SettingsChanged
        {
            add { }
            remove { }
        }

        public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<UserSettingsSnapshot> UpdateAsync(
            Func<UserSettings, UserSettings> mutate,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
