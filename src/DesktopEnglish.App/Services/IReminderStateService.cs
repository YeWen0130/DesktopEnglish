namespace DesktopEnglish.Services;

/// <summary>
/// Runtime access to the persisted pause state of reminders.
/// </summary>
public interface IReminderStateService
{
    bool IsPaused { get; }

    Task PauseAsync(CancellationToken cancellationToken);

    Task ResumeAsync(CancellationToken cancellationToken);

    event EventHandler? StateChanged;
}

/// <summary>
/// Keeps the in-memory reminder state synchronized with the persisted user settings row.
/// </summary>
public sealed class ReminderStateService : IReminderStateService, IDisposable
{
    private readonly ISettingsService _settings;

    public ReminderStateService(ISettingsService settings)
    {
        _settings = settings;
        _settings.SettingsChanged += OnSettingsChanged;
    }

    public bool IsPaused => _settings.Current.RemindersPaused;

    public event EventHandler? StateChanged;

    public Task PauseAsync(CancellationToken cancellationToken) => SetPausedAsync(true, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken) => SetPausedAsync(false, cancellationToken);

    public void Dispose() => _settings.SettingsChanged -= OnSettingsChanged;

    private async Task SetPausedAsync(bool paused, CancellationToken cancellationToken)
    {
        await _settings.UpdateAsync(entity =>
        {
            entity.RemindersPaused = paused;
            return entity;
        }, cancellationToken);
    }

    private void OnSettingsChanged(object? sender, UserSettingsSnapshot snapshot) =>
        StateChanged?.Invoke(this, EventArgs.Empty);
}
