using CommunityToolkit.WinUI.Notifications;
using DesktopEnglish.Domain;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>
/// Receives toast activation callbacks and hands them to the study service on the UI thread.
/// Toast callbacks arrive on a background thread, so nothing here touches WPF directly.
/// </summary>
public interface IToastActivationHandler
{
    void Start();

    void Stop();
}

/// <inheritdoc />
public sealed class ToastActivationHandler(
    IDispatcherService dispatcher,
    IStudySessionService studyService,
    IStudyLauncher studyLauncher,
    ILogger<ToastActivationHandler> logger) : IToastActivationHandler
{
    private bool _subscribed;

    public void Start()
    {
        if (_subscribed)
        {
            return;
        }

        ToastNotificationManagerCompat.OnActivated += OnActivated;
        _subscribed = true;
        logger.LogDebug("Toast activation handler subscribed.");
    }

    public void Stop()
    {
        if (!_subscribed)
        {
            return;
        }

        ToastNotificationManagerCompat.OnActivated -= OnActivated;
        _subscribed = false;
        logger.LogDebug("Toast activation handler unsubscribed.");
    }

    private void OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        try
        {
            var parsed = ToastArguments.Parse(args.Argument);
            var activation = Parse(parsed);

            logger.LogInformation("Toast activated: action={Action} wordId={WordId}", activation.Action, activation.WordId);

            // Fire-and-forget is intentional: the toast callback must return promptly, and any failure is
            // logged and swallowed rather than crashing the app.
            _ = HandleAsync(activation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process toast activation.");
        }
    }

    private static ReminderActivation Parse(ToastArguments args)
    {
        var action = args.TryGetValue(ToastContract.ActionKey, out var rawAction) ? rawAction : string.Empty;

        int? wordId = null;
        if (args.TryGetValue(ToastContract.WordIdKey, out var rawWordId) && int.TryParse(rawWordId, out var parsedId))
        {
            wordId = parsedId;
        }

        return new ReminderActivation(action, wordId);
    }

    private async Task HandleAsync(ReminderActivation activation)
    {
        try
        {
            var studyResult = MapResult(activation.Action);

            if (studyResult is not null && activation.WordId is int wordId)
            {
                await studyService.RecordResultAsync(wordId, studyResult.Value, CancellationToken.None);
                return;
            }

            if (string.Equals(activation.Action, ToastContract.ActionStudy, StringComparison.OrdinalIgnoreCase))
            {
                await dispatcher.InvokeAsync(async () =>
                {
                    await studyLauncher.LaunchAsync(CancellationToken.None);
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle toast activation for action {Action}.", activation.Action);
        }
    }

    private static StudyResult? MapResult(string action) =>
        action switch
        {
            ToastContract.ActionKnown => StudyResult.Known,
            ToastContract.ActionFuzzy => StudyResult.Fuzzy,
            ToastContract.ActionUnknown => StudyResult.Unknown,
            _ => null,
        };
}
