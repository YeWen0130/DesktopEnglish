namespace DesktopEnglish.Services;

/// <summary>
/// Shell actions that only the WPF application can perform. Implemented by the application class and
/// injected, so services can request window changes without any static state.
/// </summary>
public interface IShellController
{
    /// <summary>Shows or re-activates the main window.</summary>
    void ShowMainWindow();

    /// <summary>Shows the main window on the settings page.</summary>
    void ShowSettings();

    /// <summary>Shows the main window on the study page and loads a batch.</summary>
    void StartStudy();

    /// <summary>
    /// Confirms with the user and then removes the application. Windows itself drives the removal when
    /// invoked from Apps &amp; features; this entry point is for the in-app menu.
    /// </summary>
    void RequestUninstall();
}