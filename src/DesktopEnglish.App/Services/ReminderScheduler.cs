using Microsoft.Extensions.Hosting;
using DesktopEnglish.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>
/// Always-on background reminder loop. Driven by <see cref="PeriodicTimer"/> so it keeps running while the
/// main window is closed, and completely decoupled from any UI type.
/// </summary>
public sealed class ReminderScheduler(
    IReminderPlanner planner,
    INotificationService notifications,
    ISettingsService settings,
    ILogger<ReminderScheduler> logger) : BackgroundService
{
    private readonly object _timersGate = new();
    private PeriodicTimer? _timer;
    private TimeSpan _period;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(settings.Current.Interval);

        lock (_timersGate)
        {
            _period = settings.Current.Interval;
            _timer = timer;
        }

        settings.SettingsChanged += OnSettingsChanged;

        logger.LogInformation("Reminder scheduler started with interval {Interval}.", settings.Current.Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // Never let a failing tick kill the loop; reminders must survive transient faults.
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Reminder tick failed; the scheduler will keep running.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            settings.SettingsChanged -= OnSettingsChanged;

            lock (_timersGate)
            {
                _timer = null;
            }

            timer.Dispose();
            logger.LogInformation("Reminder scheduler stopped.");
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var plan = await planner.PlanAsync(cancellationToken);

        if (plan.Status != ReminderPlanStatus.Ready)
        {
            logger.LogDebug("Reminder skipped: {Status}.", plan.Status);
            return;
        }

        var notification = new WordReminderNotification(plan.Items, settings.Current.SoundEnabled);
        await notifications.ShowWordReminderAsync(notification, cancellationToken);

        logger.LogInformation("Reminder shown for {Count} word(s).", plan.Items.Count);
    }

    /// <summary>
    /// Applies an interval change immediately without restarting the app.
    /// <see cref="PeriodicTimer.Period"/> is writable on .NET 8, so the new cadence takes effect from the
    /// next tick.
    /// </summary>
    private void OnSettingsChanged(object? sender, UserSettingsSnapshot snapshot)
    {
        var desired = snapshot.Interval;
        if (_period == desired)
        {
            return;
        }

        _period = desired;

        try
        {
            lock (_timersGate)
            {
                if (_timer is not null)
                {
                    _timer.Period = desired;
                }
            }

            logger.LogInformation("Reminder interval updated to {Interval}.", desired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply the new reminder interval; it will apply on next restart.");
        }
    }
}

