using System.Reflection;
using CommunityToolkit.WinUI.Notifications;
using AppPaths = DesktopEnglish.Data.AppPaths;
using Microsoft.Extensions.Logging;

namespace DesktopEnglish.Installation;

/// <summary>
/// Registers and removes the per-user "Apps &amp; features" entry for this application.
/// </summary>
public interface IUninstallService
{
    /// <summary>Creates or refreshes the uninstall key so Windows can remove the app.</summary>
    void Register();

    /// <summary>Removes every trace the app created: registry keys, notification data and user data.</summary>
    UninstallReport Uninstall();
}

/// <summary>What an uninstall removed, so the caller can log or display it.</summary>
public sealed record UninstallReport(
    string ExecutablePath,
    string DataDirectory,
    string UninstallKeyPath,
    bool ExecutableDeletionScheduled);

/// <summary>
/// Everything an uninstall needs to remove, derived from the executable path without touching the
/// machine. Kept separate from the removal itself so the target list can be verified directly.
/// </summary>
public sealed record UninstallPlan(
    string ExecutablePath,
    string Aumid,
    string AppUserModelIdRegistryPath,
    string ActivatorRegistryPath,
    string NotificationDataDirectory,
    string AutoStartValueName,
    string UninstallKeyPath);

/// <summary>
/// The machine-touching side of an uninstall: locating the executable, deleting files and clearing the
/// notification registration. Behind an interface so the removal steps can be verified without
/// deleting anything real.
/// </summary>
public interface IUninstallEnvironment
{
    /// <summary>Absolute path of the running executable.</summary>
    string ResolveExecutablePath();

    /// <summary>Directory that holds the app's database, logs and backups.</summary>
    string DataDirectory { get; }

    /// <summary>Deletes a directory tree, tolerating files that are briefly locked.</summary>
    void DeleteDirectory(string path);

    /// <summary>Clears the notification registration owned by the current process.</summary>
    void RemoveNotificationRegistration();

    /// <summary>Schedules the executable to delete itself after this process exits.</summary>
    bool ScheduleExecutableDeletion(string executablePath);
}

/// <summary>
/// The app is portable: it runs from wherever it is placed and registers itself, rather than being
/// copied to a fixed install directory. Uninstall therefore has to remove both the registry footprint
/// and the files the app left behind under <c>%LOCALAPPDATA%</c>, and finally delete the executable.
/// </summary>
public sealed class UninstallService(
    IUninstallRegistry registry,
    IApplicationStopper stopper,
    IUninstallEnvironment environment,
    ILogger<UninstallService> logger) : IUninstallService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ToastLoaderFolder = "ToastNotificationManagerCompat";

    public void Register()
    {
        var executable = environment.ResolveExecutablePath();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        var uninstallCommand = AppInstallInfo.FormatUninstallCommand(executable);

        registry.SetString(AppInstallInfo.UninstallKeyPath, "DisplayName", AppInstallInfo.DisplayName);
        registry.SetString(AppInstallInfo.UninstallKeyPath, "DisplayVersion", version);
        registry.SetString(AppInstallInfo.UninstallKeyPath, "Publisher", AppInstallInfo.Publisher);
        registry.SetString(AppInstallInfo.UninstallKeyPath, "InstallLocation", Path.GetDirectoryName(executable) ?? string.Empty);
        registry.SetString(AppInstallInfo.UninstallKeyPath, "UninstallString", uninstallCommand);
        registry.SetString(AppInstallInfo.UninstallKeyPath, "QuietUninstallString", uninstallCommand);
        registry.SetString(AppInstallInfo.UninstallKeyPath, "DisplayIcon", $"{executable},0");
        registry.SetString(AppInstallInfo.UninstallKeyPath, "NoModify", "1");
        registry.SetString(AppInstallInfo.UninstallKeyPath, "NoRepair", "1");

        logger.LogInformation("Uninstall entry registered at HKCU\\{Key}.", AppInstallInfo.UninstallKeyPath);
    }

    public UninstallReport Uninstall()
    {
        var executable = environment.ResolveExecutablePath();
        var plan = BuildPlan(executable);
        logger.LogInformation("Uninstalling from {Executable}.", executable);

        // Close other running copies first: they lock the database and the executable itself.
        stopper.StopRunningInstances(TimeSpan.FromSeconds(10));

        // Auto start must go first, otherwise Windows would try to launch a deleted executable.
        registry.DeleteValue(RunKeyPath, plan.AutoStartValueName);

        // Then the entry that lives in Apps & features.
        registry.DeleteTree(plan.UninstallKeyPath);

        // Notification registrations: the library's own cleanup for the running process...
        SafeRemoveNotificationRegistration();

        // ...plus the entries derived from the executable path, which the library cannot know about if
        // the portable app has since moved folders.
        registry.DeleteTree(plan.AppUserModelIdRegistryPath);
        registry.DeleteTree(plan.ActivatorRegistryPath);
        environment.DeleteDirectory(plan.NotificationDataDirectory);

        // Finally the user's data (database, logs, backups).
        environment.DeleteDirectory(environment.DataDirectory);

        var deletionScheduled = environment.ScheduleExecutableDeletion(plan.ExecutablePath);

        logger.LogInformation("Uninstall finished.");
        return new UninstallReport(plan.ExecutablePath, environment.DataDirectory, plan.UninstallKeyPath, deletionScheduled);
    }

    /// <summary>
    /// Computes every path and key an uninstall must remove for the executable at
    /// <paramref name="executablePath"/>. Pure, so it can be exercised without a real install.
    /// </summary>
    public static UninstallPlan BuildPlan(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var fullPath = Path.GetFullPath(executablePath);
        var aumid = ToastIdentity.GetAumid(fullPath);
        var activatorData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ToastLoaderFolder,
            "Apps",
            ToastIdentity.GetActivatorClassId(aumid).ToString("D").ToUpperInvariant());

        return new UninstallPlan(
            ExecutablePath: fullPath,
            Aumid: aumid,
            AppUserModelIdRegistryPath: ToastIdentity.GetAppUserModelIdRegistryPath(aumid),
            ActivatorRegistryPath: ToastIdentity.GetActivatorClassRegistryPath(aumid),
            NotificationDataDirectory: activatorData,
            AutoStartValueName: AppInstallInfo.AutoStartValueName,
            UninstallKeyPath: AppInstallInfo.UninstallKeyPath);
    }

    private void SafeRemoveNotificationRegistration()
    {
        try
        {
            environment.RemoveNotificationRegistration();
        }
        catch (Exception ex)
        {
            // A failed notification cleanup must not stop the rest of the uninstall.
            logger.LogDebug(ex, "Notification library uninstall reported an issue.");
        }
    }
}

/// <summary>Production environment: the real paths and APIs an uninstall acts on.</summary>
public sealed class UninstallEnvironment : IUninstallEnvironment
{
    public string DataDirectory => AppPaths.DataDirectory;

    public string ResolveExecutablePath()
    {
        // Environment.ProcessPath is the reliable value for single-file publishes.
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Could not resolve the running executable path.");
        }

        return Path.GetFullPath(path);
    }

    public void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        // The database and log files may be locked momentarily after a running copy exits; retry briefly.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(300);
            }
        }

        // One last attempt so genuine failures surface to the caller's log.
        Directory.Delete(path, recursive: true);
    }

    public void RemoveNotificationRegistration() => ToastNotificationManagerCompat.Uninstall();

    public bool ScheduleExecutableDeletion(string executablePath)
    {
        // A process cannot delete the file it is running from, and it may still be alive while the
        // confirmation dialog waits for the user. Hand the job to a short-lived helper script that keeps
        // retrying until the lock clears - however long that takes - and then removes itself, so no
        // trace of the uninstall is left behind.
        var directory = Path.GetDirectoryName(executablePath) ?? string.Empty;

        // The standalone uninstaller ships next to the executable, so it is removed together with it.
        var uninstallerFiles = AppInstallInfo.UninstallerFileNames
            .Select(name => Path.Combine(directory, name))
            .Where(File.Exists)
            .Select(path => $"    del /f /q \"{path}\" >nul 2>&1");

        var scriptPath = Path.Combine(Path.GetTempPath(), $"DesktopEnglish-uninstall-{Guid.NewGuid():N}.cmd");
        var script = $"""
            @echo off
            setlocal
            for /l %%i in (1,1,300) do (
                del /f /q "{executablePath}" >nul 2>&1
                if not exist "{executablePath}" goto done
                ping -n 2 127.0.0.1 >nul
            )
            :done
            {string.Join(Environment.NewLine, uninstallerFiles)}
            del /f /q "%~f0" >nul 2>&1
            """;

        // cmd.exe only parses labels and blocks reliably with CRLF line endings.
        File.WriteAllText(scriptPath, script.ReplaceLineEndings("\r\n"));

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = scriptPath,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };

        return System.Diagnostics.Process.Start(startInfo) is not null;
    }
}
