using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>
/// Bridges service-layer requests ("start studying", "open settings") onto the UI thread and into the shell.
/// </summary>
public sealed class StudyLauncher(
    IShellController shell,
    IDispatcherService dispatcher,
    ILogger<StudyLauncher> logger) : IStudyLauncher
{
    public Task LaunchAsync(CancellationToken cancellationToken)
    {
        try
        {
            dispatcher.Invoke(shell.StartStudy);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open the study window.");
        }

        return Task.CompletedTask;
    }

    public void ShowMainWindow()
    {
        try
        {
            dispatcher.Invoke(shell.ShowMainWindow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to show the main window.");
        }
    }

    public void ShowSettings()
    {
        try
        {
            dispatcher.Invoke(shell.ShowSettings);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to show the settings page.");
        }
    }

    public void Uninstall()
    {
        try
        {
            dispatcher.Invoke(shell.RequestUninstall);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start the uninstall.");
        }
    }
}