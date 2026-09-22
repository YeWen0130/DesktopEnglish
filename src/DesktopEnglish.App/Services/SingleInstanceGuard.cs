using System.Threading;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>
/// Enforces a single running instance. A second launch signals the first to surface its window and then exits,
/// which prevents duplicate reminders and concurrent database writers.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Global\DesktopEnglish.SingleInstance";
    private const string EventName = @"Global\DesktopEnglish.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly ILogger<SingleInstanceGuard> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listener;
    private bool _isPrimary;
    private bool _disposed;

    public SingleInstanceGuard(ILogger<SingleInstanceGuard> logger)
    {
        _logger = logger;
        _mutex = new Mutex(initiallyOwned: false, MutexName);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
    }

    /// <summary>True when this process owns the instance lock.</summary>
    public bool IsPrimary => _isPrimary;

    /// <summary>
    /// Attempts to become the primary instance. When another instance already owns the lock, the existing
    /// instance is signalled to show its window and this returns false.
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            _isPrimary = _mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died without releasing; we are now the owner.
            _isPrimary = true;
        }

        if (!_isPrimary)
        {
            _logger.LogInformation("Another instance is already running; signalling it to show its window.");
            _showEvent.Set();
            return false;
        }

        return true;
    }

    /// <summary>Starts listening for activation signals coming from later launches.</summary>
    public void StartListening(Action onShowRequested)
    {
        ArgumentNullException.ThrowIfNull(onShowRequested);

        _listener = Task.Run(() =>
        {
            var handles = new WaitHandle[] { _showEvent, _cts.Token.WaitHandle };

            while (!_cts.IsCancellationRequested)
            {
                var signaled = WaitHandle.WaitAny(handles);

                if (_cts.IsCancellationRequested)
                {
                    return;
                }

                if (signaled != 0)
                {
                    continue;
                }

                try
                {
                    onShowRequested();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to handle a second-instance activation.");
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cts.Cancel();
        _showEvent.Set();

        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Second-instance listener did not stop cleanly.");
        }

        if (_isPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned; nothing to release.
            }
        }

        _cts.Dispose();
        _showEvent.Dispose();
        _mutex.Dispose();
    }
}
