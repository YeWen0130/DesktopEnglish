using System.Diagnostics;

namespace DesktopEnglish.Installation;

/// <summary>
/// Stops any running copies of the app. Install and uninstall both replace or delete files the running
/// process holds open, so they must ask it to quit first.
/// </summary>
public interface IApplicationStopper
{
    /// <summary>Signals running instances to exit and waits up to <paramref name="timeout"/> for them.</summary>
    void StopRunningInstances(TimeSpan timeout);
}

/// <inheritdoc />
public sealed class ApplicationStopper : IApplicationStopper
{
    private const int SwShowWindow = 1;

    public void StopRunningInstances(TimeSpan timeout)
    {
        var current = Environment.ProcessId;
        var name = AppInstallInfo.ExecutableName;
        var waiters = new List<Process>();

        foreach (var process in Process.GetProcessesByName(name))
        {
            if (process.Id == current)
            {
                process.Dispose();
                continue;
            }

            try
            {
                // The main window is hidden to the tray; showing it lets WM_CLOSE reach a normal
                // window and lets the app run its orderly shutdown. A process with no main window
                // cannot be asked to close, so it is killed instead.
                _ = ShowWindow(process.MainWindowHandle, SwShowWindow);

                if (process.CloseMainWindow() && process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    process.Dispose();
                }
                else
                {
                    waiters.Add(process);
                }
            }
            catch
            {
                // The process may have exited on its own between enumeration and signalling.
                waiters.Add(process);
            }
        }

        foreach (var process in waiters)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)timeout.TotalMilliseconds);
            }
            catch
            {
                // Nothing more can be done; the file operation will surface any real problem.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
