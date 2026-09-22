using DesktopEnglish.Domain;

namespace DesktopEnglish.Services;

/// <summary>
/// Picks the words for one reminder round and decides whether a round should happen at all.
/// Pure with respect to notification delivery: it only selects and returns.
/// </summary>
public interface IReminderPlanner
{
    Task<ReminderPlan> PlanAsync(CancellationToken cancellationToken);
}

/// <summary>Outcome of planning a reminder round.</summary>
public sealed record ReminderPlan(ReminderPlanStatus Status, IReadOnlyList<WordReminderItem> Items)
{
    public static ReminderPlan Skipped(ReminderPlanStatus status) => new(status, []);
}

public enum ReminderPlanStatus
{
    Ready,
    Disabled,
    Paused,
    DailyLimitReached,
    NoWordsAvailable,
}

/// <summary>
/// Default planner: prefers overdue reviews, tops up with unseen words, and enforces the daily count as a
/// hard limit.
/// </summary>
public sealed class ReminderPlanner(
    IWordSelectionService selection,
    ISettingsService settings,
    IReminderStateService reminderState) : IReminderPlanner
{
    public async Task<ReminderPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var current = settings.Current;

        if (!current.NotificationEnabled)
        {
            return ReminderPlan.Skipped(ReminderPlanStatus.Disabled);
        }

        if (reminderState.IsPaused)
        {
            return ReminderPlan.Skipped(ReminderPlanStatus.Paused);
        }

        var remaining = await selection.GetRemainingDailyQuotaAsync(current.DailyWordCount, cancellationToken);
        if (remaining <= 0)
        {
            return ReminderPlan.Skipped(ReminderPlanStatus.DailyLimitReached);
        }

        var batchSize = Math.Min(Math.Max(1, current.WordsPerReminder), remaining);

        var due = await selection.SelectDueReviewsAsync(batchSize, cancellationToken);
        var items = due.Select(ToItem).ToList();

        if (items.Count < batchSize)
        {
            var newWords = await selection.SelectNewWordsAsync(batchSize - items.Count, cancellationToken);
            items.AddRange(newWords.Select(ToItem));
        }

        return items.Count == 0
            ? ReminderPlan.Skipped(ReminderPlanStatus.NoWordsAvailable)
            : new ReminderPlan(ReminderPlanStatus.Ready, items);
    }

    private static WordReminderItem ToItem(Word word) => new(
        WordId: word.Id,
        WordText: word.WordText,
        Phonetic: word.Phonetic,
        PartOfSpeech: word.PartOfSpeech,
        Meaning: word.Meaning,
        Example: word.Example);
}
