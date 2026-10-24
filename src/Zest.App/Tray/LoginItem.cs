using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Zest.App.Tray;

/// <summary>"Run at Windows sign-in" via HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Zest.</summary>
public static class LoginItem
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Zest";
    const string LegacyValueName = "LimeWin";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Replaces the old app's "LimeWin" sign-in entry with Zest's own, if one exists.</summary>
    public static void MigrateLegacy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(LegacyValueName) is null) return;
            using (var write = Registry.CurrentUser.CreateSubKey(RunKey))
                write.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            Set(true);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
