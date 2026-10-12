using System.Security.Cryptography;
using System.Text;

namespace DesktopEnglish.Installation;

/// <summary>
/// Reproduces the identifiers <c>ToastNotificationManagerCompat</c> creates for an unpackaged desktop
/// app, so the uninstaller can remove exactly what the app registered.
/// </summary>
/// <remarks>
/// The library keys everything off the app's user model id (AUMID): for a desktop app that is the
/// executable path with backslashes replaced by forward slashes. Its COM activator class id is the
/// SHA-1 of the AUMID's UTF-8 bytes, with the leading sixteen bytes read as a canonical GUID. Deriving
/// it here keeps cleanup independent of the notification library at uninstall time, which matters
/// because the library only knows the running process's own AUMID and cannot account for installs that
/// have since moved.
/// </remarks>
public static class ToastIdentity
{
    /// <summary>Returns the AUMID the library would use for the executable at <paramref name="executablePath"/>.</summary>
    public static string GetAumid(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        // Match the library: forward slashes, original casing.
        return executablePath.Replace('\\', '/');
    }

    /// <summary>Returns the registry sub key the library would create for <paramref name="aumid"/>.</summary>
    public static string GetAppUserModelIdRegistryPath(string aumid) =>
        @"Software\Classes\AppUserModelId\" + aumid;

    /// <summary>Returns the COM activator class id the library would derive from <paramref name="aumid"/>.</summary>
    public static Guid GetActivatorClassId(string aumid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aumid);

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(aumid));

        // Convert.ToHexString preserves byte order, and Guid's canonical string form reads those bytes
        // big endian, so parsing the formatted hex yields exactly the library's GUID.
        var hex = Convert.ToHexString(hash.AsSpan(0, 16));
        return Guid.ParseExact(hex, "N");
    }

    /// <summary>Returns the <c>HKCU\Software\Classes\CLSID\{...}</c> path for the activator.</summary>
    public static string GetActivatorClassRegistryPath(string aumid) =>
        $@"Software\Classes\CLSID\{{{GetActivatorClassId(aumid):D}}}";
}
