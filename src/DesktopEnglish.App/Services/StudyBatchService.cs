namespace DesktopEnglish.Services;

/// <summary>
/// Builds the batch of words shown in the study window. Shared by reminder-driven and manual ("立即学习")
/// sessions so both paths behave identically.
/// </summary>
public interface IStudyBatchService
{
    /// <summary>
    /// Builds a batch preferring due reviews and topping up with new words.
    /// <paramref name="respectDailyLimit"/> is false for manual sessions, which the user may start at any time.
    /// </summary>
    Task<IReadOnlyList<WordReminderItem>> BuildBatchAsync(
        int size,
        bool respectDailyLimit,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class StudyBatchService(
    IWordSelectionService selection,
    ISettingsService settings) : IStudyBatchService
{
    public async Task<IReadOnlyList<WordReminderItem>> BuildBatchAsync(
        int size,
        bool respectDailyLimit,
        CancellationToken cancellationToken)
    {
        var current = settings.Current;
        var desired = Math.Max(1, size);

        if (respectDailyLimit)
        {
            var remaining = await selection.GetRemainingDailyQuotaAsync(current.DailyWordCount, cancellationToken);
            if (remaining <= 0)
            {
                return [];
            }

            desired = Math.Min(desired, remaining);
        }

        var due = await selection.SelectDueReviewsAsync(desired, cancellationToken);
        var items = due.Select(ToItem).ToList();

        if (items.Count < desired)
        {
            var newWords = await selection.SelectNewWordsAsync(desired - items.Count, cancellationToken);
            items.AddRange(newWords.Select(ToItem));
        }

        return items;
    }

    private static WordReminderItem ToItem(Domain.Word word) => new(
        WordId: word.Id,
        WordText: word.WordText,
        Phonetic: word.Phonetic,
        PartOfSpeech: word.PartOfSpeech,
        Meaning: word.Meaning,
        Example: word.Example);
}
