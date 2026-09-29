using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Xunit;

namespace DesktopEnglish.Tests;

public class AdaptiveReviewSchedulerTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Sm2_known_sequence_uses_one_then_six_days()
    {
        var scheduler = new Sm2ReviewScheduler();
        var state = scheduler.CreateInitial(Now);

        state = scheduler.Apply(state, StudyResult.Known, Now);
        Assert.Equal(Now.AddDays(1), state.NextReviewTime);

        state = scheduler.Apply(state, StudyResult.Known, Now.AddDays(1));
        Assert.Equal(Now.AddDays(7), state.NextReviewTime);
        Assert.Equal(2, state.Sm2Repetitions);
    }

    [Fact]
    public void Sm2_unknown_resets_repetitions_and_never_drops_below_minimum_ease()
    {
        var scheduler = new Sm2ReviewScheduler();
        var state = scheduler.CreateInitial(Now) with
        {
            Sm2Repetitions = 5,
            Sm2EaseFactor = 1.3,
            Sm2IntervalDays = 30,
        };

        state = scheduler.Apply(state, StudyResult.Unknown, Now);

        Assert.Equal(0, state.Sm2Repetitions);
        Assert.Equal(1.3, state.Sm2EaseFactor);
        Assert.Equal(Now.AddDays(1), state.NextReviewTime);
        Assert.Equal(0, state.MasteryLevel);
    }

    [Fact]
    public void Fsrs_again_is_due_in_ten_minutes_and_resets_mastery()
    {
        var scheduler = new FsrsReviewScheduler();
        var state = scheduler.CreateInitial(Now);

        state = scheduler.Apply(state, StudyResult.Unknown, Now, 0.9);

        Assert.Equal(Now.AddMinutes(10), state.NextReviewTime);
        Assert.Equal(ReviewAlgorithm.Fsrs, state.Algorithm);
        Assert.Equal(0, state.MasteryLevel);
        Assert.True(state.FsrsStability > 0);
    }

    [Fact]
    public void Fsrs_first_good_uses_the_pinned_version_six_defaults()
    {
        var scheduler = new FsrsReviewScheduler();

        var state = scheduler.Apply(scheduler.CreateInitial(Now), StudyResult.Known, Now, 0.9);

        Assert.Equal(6, state.AlgorithmVersion);
        Assert.Equal(2.3065, state.FsrsStability, 4);
        Assert.Equal(2.3065, state.Sm2IntervalDays, 4);
    }

    [Theory]
    [InlineData(0.8, 7.648261246942)]
    [InlineData(0.9, 2.3065)]
    [InlineData(0.97, 0.513815408578)]
    public void Fsrs6_interval_matches_the_official_default_parameter_vector(double retention, double expectedDays)
    {
        var scheduler = new FsrsReviewScheduler();

        var state = scheduler.Apply(scheduler.CreateInitial(Now), StudyResult.Known, Now, retention);

        Assert.Equal(expectedDays, state.Sm2IntervalDays, 9);
        Assert.Equal(Now.AddDays(expectedDays), state.NextReviewTime, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Higher_fsrs_retention_produces_a_shorter_interval()
    {
        var scheduler = new FsrsReviewScheduler();
        var current = scheduler.CreateInitial(Now) with { FsrsStability = 10, FsrsDifficulty = 5 };

        var normal = scheduler.Apply(current, StudyResult.Known, Now.AddDays(5), 0.9);
        var high = scheduler.Apply(current, StudyResult.Known, Now.AddDays(5), 0.97);

        Assert.True(high.NextReviewTime < normal.NextReviewTime);
    }

    [Fact]
    public void Same_timestamp_replay_remains_finite()
    {
        var scheduler = new FsrsReviewScheduler();
        var state = scheduler.CreateInitial(Now);

        state = scheduler.Apply(state, StudyResult.Known, Now, 0.9);
        state = scheduler.Apply(state, StudyResult.Fuzzy, Now, 0.9);

        Assert.True(double.IsFinite(state.FsrsStability));
        Assert.True(state.NextReviewTime > Now);
    }
}
