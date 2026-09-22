using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Services;

/// <summary>Controls whether the app starts with Windows.</summary>
public interface IStartupService
{
    bool IsEnabled();

    void Apply(bool enabled);
}

/// <summary>
/// Registers the app under the per-user Run key. Uses <see cref="Environment.ProcessPath"/> because
/// <c>Assembly.Location</c> is empty for single-file published apps.
/// </summary>
public sealed class StartupService(ILogger<StartupService> logger) : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopEnglish";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read the startup registry value.");
            return false;
        }
    }

    public void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                logger.LogWarning("Startup registry key could not be opened; AutoStart was not changed.");
                return;
            }

            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable))
                {
                    logger.LogWarning("Executable path could not be resolved; AutoStart was not enabled.");
                    return;
                }

                key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
                logger.LogInformation("AutoStart enabled for {Executable}.", executable);
            }
            else
            {
                if (key.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                logger.LogInformation("AutoStart disabled.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update the startup registry value.");
        }
    }
}
