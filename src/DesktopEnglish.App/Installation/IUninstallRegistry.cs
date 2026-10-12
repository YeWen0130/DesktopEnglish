using Microsoft.Win32;

namespace DesktopEnglish.Installation;

/// <summary>
/// Thin seam over the Windows registry so the installer and uninstaller can be exercised without
/// touching a real machine's registry.
/// </summary>
public interface IUninstallRegistry
{
    /// <summary>Creates (or overwrites) a string value under <paramref name="subKey"/>.</summary>
    void SetString(string subKey, string name, string value);

    /// <summary>Deletes a single value; missing keys or values are not an error.</summary>
    void DeleteValue(string subKey, string name);

    /// <summary>Deletes a sub key and everything under it; a missing key is not an error.</summary>
    void DeleteTree(string subKey);
}

/// <summary>The production implementation, operating on <c>HKEY_CURRENT_USER</c>.</summary>
public sealed class CurrentUserRegistry : IUninstallRegistry
{
    public void SetString(string subKey, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Could not create HKCU\\{subKey}.");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteTree(string subKey) =>
        Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
}
