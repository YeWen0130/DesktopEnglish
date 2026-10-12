namespace DesktopEnglish.Services;

/// <summary>
/// Opens the study experience. Implemented in the UI layer so services can request a study session without
/// referencing any window type.
/// </summary>
public interface IStudyLauncher
{
    Task LaunchAsync(CancellationToken cancellationToken);

    /// <summary>Raises the main window (used by tray menu and second-instance activation).</summary>
    void ShowMainWindow();

    /// <summary>Opens the settings page.</summary>
    void ShowSettings();

    /// <summary>Confirms and removes the application (used by the tray menu).</summary>
    void Uninstall();
}