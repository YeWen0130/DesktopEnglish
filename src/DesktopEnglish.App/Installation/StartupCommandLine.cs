namespace DesktopEnglish.Installation;

/// <summary>How a launch should behave, derived from the command line.</summary>
public enum StartupMode
{
    /// <summary>A normal launch: run in the tray and remind.</summary>
    Run,

    /// <summary>Remove the application, then exit without touching the database or the tray.</summary>
    Uninstall,
}

/// <summary>Parses the switches the shipped executable accepts.</summary>
public static class StartupCommandLine
{
    /// <summary>
    /// Reads the startup mode. Recognises <c>--uninstall</c> and <c>/uninstall</c>, case insensitively,
    /// because Windows invokes the uninstall command verbatim from the registry.
    /// </summary>
    public static StartupMode Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        foreach (var arg in args)
        {
            if (string.Equals(arg.TrimStart('-', '/'), "uninstall", StringComparison.OrdinalIgnoreCase))
            {
                return StartupMode.Uninstall;
            }
        }

        return StartupMode.Run;
    }
}
