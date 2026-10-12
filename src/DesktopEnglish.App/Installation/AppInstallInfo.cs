namespace DesktopEnglish.Installation;

/// <summary>
/// Static identity of the shipped application, shared by every registration/uninstall path so the
/// registry values stay consistent. Nothing here is derived at runtime: these are the literal strings
/// that end up in the registry.
/// </summary>
public static class AppInstallInfo
{
    /// <summary>Executable file name (without extension) produced by the publish step.</summary>
    public const string ExecutableName = "DesktopEnglish";

    /// <summary>Name shown in Windows "Apps &amp; features".</summary>
    public const string DisplayName = "背单词";

    /// <summary>Value the app writes under the per-user Run key.</summary>
    public const string AutoStartValueName = "DesktopEnglish";

    /// <summary>
    /// The value written to the per-user Uninstall key. Kept stable across versions and install
    /// locations so Windows always shows a single entry.
    /// </summary>
    public const string UninstallRegistryKeyName = "DesktopEnglish";

    /// <summary>Publisher shown on the Apps &amp; features page.</summary>
    public const string Publisher = "DesktopEnglish";

    /// <summary>Full uninstall key path under HKEY_CURRENT_USER.</summary>
    public const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + UninstallRegistryKeyName;

    /// <summary>Command Windows runs to uninstall: the executable with the uninstall switch.</summary>
    public const string UninstallSwitch = "--uninstall";

    /// <summary>Formats the uninstall command for a given executable path.</summary>
    public static string FormatUninstallCommand(string executablePath) => $"\"{executablePath}\" {UninstallSwitch}";
}
