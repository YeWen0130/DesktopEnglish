using CommunityToolkit.WinUI.Notifications;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>
/// Renders reminders as Windows toast notifications via <c>ToastNotificationManagerCompat</c>, which also
/// supplies the AUMID registration unpackaged desktop apps need.
/// Falls back to a tray balloon when toast delivery is unavailable, so a reminder is never silently lost.
/// </summary>
public sealed class ToastNotificationService(
    ILogger<ToastNotificationService> logger) : INotificationService
{
    private readonly object _gate = new();
    private Action<string, string>? _balloonFallback;

    /// <summary>
    /// Supplies the tray balloon fallback. Registered by the tray host so this service stays free of UI types.
    /// </summary>
    public void UseBalloonFallback(Action<string, string> fallback)
    {
        lock (_gate)
        {
            _balloonFallback = fallback;
        }
    }

    public Task ShowWordReminderAsync(WordReminderNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Items.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Toast construction and delivery go through WinRT/COM activation (RoGetActivationFactory), which
        // blocks on first use and would freeze the WPF message loop if it ran on the UI thread.
        return Task.Run(() => ShowCore(notification), cancellationToken);
    }

    private void ShowCore(WordReminderNotification notification)
    {
        try
        {
            var builder = new ToastContentBuilder();

            if (notification.IsSingle)
            {
                BuildSingleWordToast(builder, notification.Items[0], notification.PlaySound);
            }
            else
            {
                BuildBatchToast(builder, notification, notification.PlaySound);
            }

            builder.Show();

            logger.LogInformation("Toast notification shown for {Count} word(s).", notification.Items.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Toast notification failed; falling back to a tray balloon.");

            var title = "背单词提醒";
            var body = notification.IsSingle
                ? $"{notification.Items[0].WordText} {notification.Items[0].Phonetic}".Trim()
                : $"有 {notification.Items.Count} 个单词待学习";

            Action<string, string>? fallback;
            lock (_gate)
            {
                fallback = _balloonFallback;
            }

            fallback?.Invoke(title, body);
        }
    }

    /// <summary>
    /// A single word gets the full answer set inline so the user can respond straight from the toast.
    /// </summary>
    private static void BuildSingleWordToast(ToastContentBuilder builder, WordReminderItem item, bool playSound)
    {
        var heading = item.Phonetic.Length > 0
            ? $"{item.WordText}  {item.Phonetic}"
            : item.WordText;

        builder.AddText(heading);

        var detail = string.Join("  ", new[] { item.PartOfSpeech, item.Meaning }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (detail.Length > 0)
        {
            builder.AddText(detail);
        }
        else
        {
            builder.AddText("请回忆这个单词的中文释义");
        }

        builder
            .AddButton(new ToastButton()
                .SetContent("认识")
                .AddArgument(ToastContract.ActionKey, ToastContract.ActionKnown)
                .AddArgument(ToastContract.WordIdKey, item.WordId))
            .AddButton(new ToastButton()
                .SetContent("模糊")
                .AddArgument(ToastContract.ActionKey, ToastContract.ActionFuzzy)
                .AddArgument(ToastContract.WordIdKey, item.WordId))
            .AddButton(new ToastButton()
                .SetContent("不认识")
                .AddArgument(ToastContract.ActionKey, ToastContract.ActionUnknown)
                .AddArgument(ToastContract.WordIdKey, item.WordId));

        builder.AddArgument(ToastContract.ActionKey, ToastContract.ActionStudy);
        ApplySound(builder, playSound);
    }

    /// <summary>
    /// Several words at once would mean several toasts; instead show one summary that opens the study window.
    /// </summary>
    private static void BuildBatchToast(ToastContentBuilder builder, WordReminderNotification notification, bool playSound)
    {
        builder.AddText($"有 {notification.Items.Count} 个单词待学习");

        var preview = string.Join("、", notification.Items.Take(3).Select(i => i.WordText));
        if (notification.Items.Count > 3)
        {
            preview += " 等";
        }

        builder.AddText(preview);

        builder
            .AddButton(new ToastButton()
                .SetContent("开始学习")
                .AddArgument(ToastContract.ActionKey, ToastContract.ActionStudy))
            .AddArgument(ToastContract.ActionKey, ToastContract.ActionStudy);

        ApplySound(builder, playSound);
    }

    private static void ApplySound(ToastContentBuilder builder, bool playSound)
    {
        if (!playSound)
        {
            // Silent toasts still render, they just do not interrupt with audio.
            builder.AddAudio(new ToastAudio { Silent = true });
        }
    }
}