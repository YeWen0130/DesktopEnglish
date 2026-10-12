using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using DesktopEnglish.Services;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Views;

/// <summary>
/// Owns the system tray icon and its menu. Kept apart from <see cref="App"/> so shutdown ordering stays clear.
/// </summary>
public sealed class TrayHost : IAsyncDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly IServiceProvider _services;
    private readonly ILogger<TrayHost> _logger;
    private readonly ToastNotificationService _toasts;
    private readonly CancellationTokenSource _cts = new();

    private TrayHost(
        TaskbarIcon icon,
        IServiceProvider services,
        ToastNotificationService toasts,
        ILogger<TrayHost> logger)
    {
        _icon = icon;
        _services = services;
        _toasts = toasts;
        _logger = logger;
    }

    /// <summary>Creates the tray icon and wires its menu to application actions.</summary>
    public static TrayHost Create(
        IServiceProvider services,
        ToastNotificationService toasts,
        ILogger<TrayHost> logger)
    {
        var reminderState = services.GetRequiredService<IReminderStateService>();
        var launcher = services.GetRequiredService<IStudyLauncher>();

        var pauseItem = new MenuItem { Header = "暂停提醒" };
        pauseItem.Click += async (_, _) => await reminderState.PauseAsync(CancellationToken.None);

        var resumeItem = new MenuItem { Header = "恢复提醒" };
        resumeItem.Click += async (_, _) => await reminderState.ResumeAsync(CancellationToken.None);

        var menu = new ContextMenu();
        menu.Items.Add(CreateItem("打开程序", () => launcher.ShowMainWindow()));
        menu.Items.Add(CreateItem("立即学习", () => _ = launcher.LaunchAsync(CancellationToken.None)));
        menu.Items.Add(new Separator());
        menu.Items.Add(pauseItem);
        menu.Items.Add(resumeItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("打开设置", () => launcher.ShowSettings()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("卸载程序", () => launcher.Uninstall()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("退出程序", Shutdown));

        var icon = new TaskbarIcon
        {
            ToolTipText = "背单词 DesktopEnglish",
            ContextMenu = menu,
            // The .ico is also compiled as an assembly resource so this works in single-file publish.
            IconSource = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute)),
            MenuActivation = H.NotifyIcon.Core.PopupActivationMode.RightClick,
            LeftClickCommand = new RelayCommand(() => launcher.ShowMainWindow()),
        };

        var host = new TrayHost(icon, services, toasts, logger);

        // The notification service falls back to a tray balloon if toast delivery fails.
        // The fallback is invoked from a background thread, but the tray icon is an STA object.
        var dispatcher = services.GetRequiredService<IDispatcherService>();

        toasts.UseBalloonFallback((title, message) =>
        {
            try
            {
                dispatcher.Invoke(() => icon.ShowNotification(title, message, H.NotifyIcon.Core.NotificationIcon.Info));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tray balloon fallback failed.");
            }
        });

        reminderState.StateChanged += (_, _) => host.UpdateMenuState(pauseItem, resumeItem);

        return host;
    }

    public void Show()
    {
        try
        {
            _icon.ForceCreate();
            _logger.LogInformation("Tray icon created.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create the tray icon.");
        }
    }

    private void UpdateMenuState(MenuItem pauseItem, MenuItem resumeItem)
    {
        try
        {
            var paused = _services.GetRequiredService<IReminderStateService>().IsPaused;
            pauseItem.IsEnabled = !paused;
            resumeItem.IsEnabled = paused;

            _icon.ToolTipText = paused ? "背单词 DesktopEnglish（已暂停提醒）" : "背单词 DesktopEnglish";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update the tray menu state.");
        }
    }

    private static MenuItem CreateItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static void Shutdown()
    {
        try
        {
            (Application.Current as App)?.RequestShutdown();
        }
        catch
        {
            Application.Current?.Shutdown();
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _cts.Cancel();
            _cts.Dispose();
            _icon.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tray icon disposal encountered an issue.");
        }

        return ValueTask.CompletedTask;
    }
}

