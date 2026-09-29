using DesktopEnglish.Domain;

namespace DesktopEnglish.Services;

internal static class ReviewOutcomeFactory
{
    public static int MasteryFromInterval(double days) => days switch
    {
        < 2 => 0,
        < 4 => 1,
        < 7 => 2,
        < 15 => 3,
        < 30 => 4,
        _ => 5,
    };

    public static ReviewOutcome Counts(ReviewOutcome current, StudyResult result, DateTime nowUtc,
        int stage, DateTime next, ReviewAlgorithm algorithm, int repetitions = 0,
        double ease = 2.5, double interval = 0, double stability = 0, double difficulty = 0) =>
        new(
            Stage: stage,
            CorrectCount: current.CorrectCount + (result == StudyResult.Unknown ? 0 : 1),
            WrongCount: current.WrongCount + (result == StudyResult.Unknown ? 1 : 0),
            MasteryLevel: Math.Clamp(ReviewOutcomeFactory.MasteryFromInterval(interval), 0, 5),
            NextReviewTime: next,
            ReviewedAt: nowUtc,
            Algorithm: algorithm,
            AlgorithmVersion: 1,
            Sm2Repetitions: repetitions,
            Sm2EaseFactor: ease,
            Sm2IntervalDays: interval,
            FsrsStability: stability,
            FsrsDifficulty: difficulty);
}

public sealed class Sm2ReviewScheduler : IReviewScheduler
{
    public int MaxStage => 6;
    public int MasteredLevel => 5;

    public ReviewOutcome CreateInitial(DateTime nowUtc) => new(
        0, 0, 0, 0, nowUtc, nowUtc, ReviewAlgorithm.Sm2, 1, 0, 2.5, 0, 0, 0);

    public ReviewOutcome Apply(ReviewOutcome current, StudyResult result, DateTime nowUtc)
    {
        var quality = result switch { StudyResult.Known => 4, StudyResult.Fuzzy => 3, _ => 1 };
        var ease = Math.Max(1.3, current.Sm2EaseFactor + 0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02));
        var repetitions = quality < 3 ? 0 : current.Sm2Repetitions + 1;
        var interval = quality < 3
            ? 1
            : repetitions switch
            {
                1 => 1,
                2 => 6,
                _ => Math.Max(1, Math.Round(Math.Max(1, current.Sm2IntervalDays) * ease, 1)),
            };

        return ReviewOutcomeFactory.Counts(current, result, nowUtc,
            ReviewOutcomeFactory.MasteryFromInterval(interval), nowUtc.AddDays(interval),
            ReviewAlgorithm.Sm2, repetitions, ease, interval);
    }
}

/// <summary>FSRS-6 compatible state model with the published default parameter set.</summary>
public sealed class FsrsReviewScheduler : IReviewScheduler
{
    private const double DefaultRetention = 0.9;
    private const double MinimumStability = 0.0001;
    private const double MaximumStability = 36500;
    private static readonly double[] Weights =
        [0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001, 1.8722, 0.1666,
         0.796, 1.4835, 0.0614, 0.2629, 1.6483, 0.6014, 1.8729, 0.5425, 0.0912, 0.0658, 0.1542];

    public int MaxStage => 6;
    public int MasteredLevel => 5;

    public ReviewOutcome CreateInitial(DateTime nowUtc) => CreateInitial(nowUtc, DefaultRetention);

    public ReviewOutcome CreateInitial(DateTime nowUtc, double desiredRetention) => new(
        0, 0, 0, 0, nowUtc, nowUtc, ReviewAlgorithm.Fsrs, 6, 0, 2.5, 0, 0, 0);

    public ReviewOutcome Apply(ReviewOutcome current, StudyResult result, DateTime nowUtc) =>
        Apply(current, result, nowUtc, DefaultRetention);

    public ReviewOutcome Apply(ReviewOutcome current, StudyResult result, DateTime nowUtc, double desiredRetention)
    {
        desiredRetention = Math.Clamp(desiredRetention, 0.80, 0.97);
        var grade = result switch { StudyResult.Known => 3, StudyResult.Fuzzy => 2, _ => 1 };
        var elapsedDays = current.ReviewedAt == default
            ? 0
            : Math.Max(0, Math.Floor((nowUtc - current.ReviewedAt).TotalDays + 0.5));
        var stability = current.FsrsStability > 0
            ? Math.Clamp(current.FsrsStability, MinimumStability, MaximumStability)
            : Weights[grade - 1];
        var difficulty = current.FsrsDifficulty > 0
            ? Math.Clamp(current.FsrsDifficulty, 1, 10)
            : InitialDifficulty(grade);

        if (current.FsrsStability > 0)
        {
            var retrievability = ForgettingCurve(elapsedDays, stability);
            var shortTerm = Math.Exp(Weights[17] * (grade - 3 + Weights[18])) *
                Math.Pow(stability, -Weights[19]);

            if (elapsedDays == 0)
            {
                stability = grade >= 2
                    ? stability * Math.Max(1, shortTerm)
                    : stability * shortTerm;
            }
            else if (grade == 1)
            {
                var minimum = stability / Math.Exp(Weights[17] * Weights[18]);
                var forgotten = Weights[11] * Math.Pow(difficulty, -Weights[12]) *
                    (Math.Pow(stability + 1, Weights[13]) - 1) *
                    Math.Exp(Weights[14] * (1 - retrievability));
                stability = Math.Min(forgotten, minimum);
            }
            else
            {
                var hardPenalty = grade == 2 ? Weights[15] : 1;
                var easyBonus = grade == 4 ? Weights[16] : 1;
                stability *= 1 + Math.Exp(Weights[8]) * (11 - difficulty) *
                    Math.Pow(stability, -Weights[9]) *
                    (Math.Exp((1 - retrievability) * Weights[10]) - 1) * hardPenalty * easyBonus;
            }

            stability = Math.Clamp(stability, MinimumStability, MaximumStability);
            difficulty = NextDifficulty(difficulty, grade);
        }

        var interval = NextInterval(stability, desiredRetention);
        var next = grade == 1 ? nowUtc.AddMinutes(10) : nowUtc.AddDays(Math.Min(3650, interval));
        var masteryInterval = grade == 1 ? 0 : interval;
        return ReviewOutcomeFactory.Counts(current, result, nowUtc,
            ReviewOutcomeFactory.MasteryFromInterval(masteryInterval), next,
            ReviewAlgorithm.Fsrs, ease: 2.5, interval: interval,
            stability: stability, difficulty: difficulty) with { AlgorithmVersion = 6 };
    }

    private static double ForgettingCurve(double elapsedDays, double stability)
    {
        var decay = -Weights[20];
        var factor = Math.Pow(0.9, 1 / decay) - 1;
        return Math.Pow(1 + factor * elapsedDays / stability, decay);
    }

    private static double NextInterval(double stability, double desiredRetention)
    {
        var decay = -Weights[20];
        var factor = Math.Pow(0.9, 1 / decay) - 1;
        return Math.Clamp(
            stability / factor * (Math.Pow(desiredRetention, 1 / decay) - 1),
            0,
            MaximumStability);
    }

    private static double InitialDifficulty(int grade) =>
        Math.Clamp(Weights[4] - Math.Exp(Weights[5] * (grade - 1)) + 1, 1, 10);

    private static double NextDifficulty(double difficulty, int grade)
    {
        var delta = -Weights[6] * (grade - 3);
        var damped = delta * (10 - difficulty) / 9;
        var next = difficulty + damped;
        var initialEasy = InitialDifficulty(4);
        return Math.Clamp(Weights[7] * initialEasy + (1 - Weights[7]) * next, 1, 10);
    }
}

public sealed class ConfiguredReviewScheduler(ISettingsService settings) : IReviewScheduler
{
    private readonly ReviewScheduler _classic = new();
    private readonly Sm2ReviewScheduler _sm2 = new();
    private readonly FsrsReviewScheduler _fsrs = new();

    public int MaxStage => _classic.MaxStage;
    public int MasteredLevel => _classic.MasteredLevel;

    public ReviewAlgorithm CurrentAlgorithm => settings.Current.ReviewAlgorithm;

    public double DesiredRetention => settings.Current.FsrsDesiredRetention;

    public ReviewOutcome CreateInitial(DateTime nowUtc) => Current().CreateInitial(nowUtc);

    public ReviewOutcome CreateInitial(DateTime nowUtc, ReviewAlgorithm algorithm, double desiredRetention) => algorithm switch
    {
        ReviewAlgorithm.Sm2 => _sm2.CreateInitial(nowUtc),
        ReviewAlgorithm.Fsrs => _fsrs.CreateInitial(nowUtc, desiredRetention),
        _ => _classic.CreateInitial(nowUtc),
    };

    public ReviewOutcome Apply(ReviewOutcome current, StudyResult result, DateTime nowUtc) => Current().Apply(current, result, nowUtc);

    private IReviewScheduler Current() => settings.Current.ReviewAlgorithm switch
    {
        ReviewAlgorithm.Sm2 => _sm2,
        ReviewAlgorithm.Fsrs => _fsrs,
        _ => _classic,
    };

    public ReviewOutcome Apply(ReviewOutcome current, StudyResult result, DateTime nowUtc,
        ReviewAlgorithm algorithm, double desiredRetention)
    {
        return algorithm switch
        {
            ReviewAlgorithm.Sm2 => _sm2.Apply(current, result, nowUtc),
            ReviewAlgorithm.Fsrs => _fsrs.Apply(current, result, nowUtc, desiredRetention),
            _ => _classic.Apply(current, result, nowUtc),
        };
    }
}
