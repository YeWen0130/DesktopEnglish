namespace DesktopEnglish.Services;

/// <summary>
/// Runtime pause/resume of reminders. Intentionally not persisted: restarting the app resumes reminders.
/// </summary>
public interface IReminderStateService
{
    bool IsPaused { get; }

    void Pause();

    void Resume();

    event EventHandler? StateChanged;
}

/// <summary>Thread-safe in-memory reminder pause state.</summary>
public sealed class ReminderStateService : IReminderStateService
{
    private readonly object _gate = new();
    private bool _isPaused;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _isPaused;
            }
        }
    }

    public event EventHandler? StateChanged;

    public void Pause() => SetPaused(true);

    public void Resume() => SetPaused(false);

    private void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_isPaused == paused)
            {
                return;
            }

            _isPaused = paused;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
