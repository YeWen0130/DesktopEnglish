using DesktopEnglish.Domain;

namespace DesktopEnglish.Services;

/// <summary>A single word surfaced by a reminder.</summary>
public sealed record WordReminderItem(
    int WordId,
    string WordText,
    string Phonetic,
    string PartOfSpeech,
    string Meaning,
    string Example);

/// <summary>
/// Everything the notification layer needs to render a reminder. Deliberately free of persistence and
/// study logic so notification rendering stays decoupled from word selection and answer handling.
/// </summary>
public sealed record WordReminderNotification(IReadOnlyList<WordReminderItem> Items, bool PlaySound)
{
    public bool IsSingle => Items.Count == 1;
}

/// <summary>
/// Renders reminder notifications. Implementations must not select words or record study results.
/// </summary>
public interface INotificationService
{
    Task ShowWordReminderAsync(WordReminderNotification notification, CancellationToken cancellationToken);
}

/// <summary>Argument keys and action names shared between the toast payload and its activation handler.</summary>
public static class ToastContract
{
    public const string ActionKey = "action";
    public const string WordIdKey = "wordId";

    public const string ActionKnown = "known";
    public const string ActionFuzzy = "fuzzy";
    public const string ActionUnknown = "unknown";
    public const string ActionStudy = "study";

    public const string ActionSourceKey = "source";
}

/// <summary>Payload raised when the user interacts with a reminder notification.</summary>
public sealed record ReminderActivation(string Action, int? WordId);
