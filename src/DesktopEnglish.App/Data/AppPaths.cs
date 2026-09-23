namespace DesktopEnglish.Data;

/// <summary>
/// Resolves the on-disk locations the application owns under <c>%LOCALAPPDATA%</c>.
/// Keeps the install directory clean and stays writable when the app is published read-only.
/// </summary>
public static class AppPaths
{
    private const string FolderName = "DesktopEnglish";

    /// <summary>Root data directory, created on demand.</summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string DatabasePath => Path.Combine(DataDirectory, "desktopenglish.db");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Where copies of a database that failed its integrity check are preserved.</summary>
    public static string BackupDirectory => Path.Combine(DataDirectory, "backup");

    /// <summary>Optional user override of the application configuration.</summary>
    public static string UserSettingsJsonPath => Path.Combine(DataDirectory, "appsettings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }

    private static string ResolveDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            // Never expected on Windows; fall back to the executable directory rather than failing to start.
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, FolderName);
    }
}
