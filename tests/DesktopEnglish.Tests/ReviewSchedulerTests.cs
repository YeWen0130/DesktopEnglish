using System.Globalization;
using System.Text;
using DesktopEnglish.Domain;
using DesktopEnglish.Services;
using Xunit;

namespace DesktopEnglish.Tests;

/// <summary>
/// Verifies the interval-repetition rules: known answers advance, unknown answers step back, fuzzy answers
/// hold, and both ends of the ladder are clamped.
/// </summary>
public class ReviewSchedulerTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly ReviewScheduler _scheduler = new();

    [Fact]
    public void CreateInitial_is_due_immediately_at_stage_zero()
    {
        var initial = _scheduler.CreateInitial(Now);

        Assert.Equal(0, initial.Stage);
        Assert.Equal(0, initial.MasteryLevel);
        Assert.Equal(Now, initial.NextReviewTime);
        Assert.Equal(0, initial.CorrectCount);
        Assert.Equal(0, initial.WrongCount);
    }

    [Fact]
    public void Known_advances_one_stage()
    {
        var current = _scheduler.CreateInitial(Now) with { Stage = 2, CorrectCount = 1, MasteryLevel = 2 };

        var next = _scheduler.Apply(current, StudyResult.Known, Now);

        Assert.Equal(3, next.Stage);
        Assert.Equal(2, next.CorrectCount);
        Assert.Equal(0, next.WrongCount);
        Assert.Equal(3, next.MasteryLevel);
        Assert.Equal(Now.AddDays(7), next.NextReviewTime);
    }

    [Fact]
    public void Fuzzy_holds_the_stage_but_counts_as_correct()
    {
        var current = _scheduler.CreateInitial(Now) with { Stage = 3, CorrectCount = 2, MasteryLevel = 3 };

        var next = _scheduler.Apply(current, StudyResult.Fuzzy, Now);

        Assert.Equal(3, next.Stage);
        Assert.Equal(3, next.CorrectCount);
        Assert.Equal(0, next.WrongCount);
        Assert.Equal(Now.AddDays(7), next.NextReviewTime);
    }

    [Fact]
    public void Unknown_steps_back_and_counts_as_wrong()
    {
        var current = _scheduler.CreateInitial(Now) with { Stage = 4, WrongCount = 1, MasteryLevel = 4 };

        var next = _scheduler.Apply(current, StudyResult.Unknown, Now);

        Assert.Equal(3, next.Stage);
        Assert.Equal(2, next.WrongCount);
        Assert.Equal(3, next.MasteryLevel);
        Assert.Equal(Now.AddDays(7), next.NextReviewTime);
    }

    [Fact]
    public void Known_does_not_exceed_the_last_stage()
    {
        var current = _scheduler.CreateInitial(Now) with { Stage = _scheduler.MaxStage };

        var next = _scheduler.Apply(current, StudyResult.Known, Now);

        Assert.Equal(_scheduler.MaxStage, next.Stage);
        Assert.Equal(_scheduler.MasteredLevel, next.MasteryLevel);
    }

    [Fact]
    public void Unknown_does_not_go_below_the_first_stage()
    {
        var current = _scheduler.CreateInitial(Now) with { Stage = 0 };

        var next = _scheduler.Apply(current, StudyResult.Unknown, Now);

        Assert.Equal(0, next.Stage);
        Assert.Equal(0, next.MasteryLevel);
        Assert.Equal(Now.AddDays(1), next.NextReviewTime);
    }

    [Fact]
    public void A_word_is_mastered_once_it_reaches_stage_five()
    {
        var current = _scheduler.CreateInitial(Now);

        var stage4 = _scheduler.Apply(current, StudyResult.Known, Now);
        stage4 = _scheduler.Apply(stage4, StudyResult.Known, Now);
        stage4 = _scheduler.Apply(stage4, StudyResult.Known, Now);
        stage4 = _scheduler.Apply(stage4, StudyResult.Known, Now);
        stage4 = _scheduler.Apply(stage4, StudyResult.Known, Now);

        Assert.Equal(5, stage4.Stage);
        Assert.Equal(5, stage4.MasteryLevel);
    }
}

