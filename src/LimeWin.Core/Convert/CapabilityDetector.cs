using LimeWin.Core.Catalog;
using Microsoft.Win32;

namespace LimeWin.Core.Convert;

/// <summary>Works out which optional engines this machine has. Call once at startup; probing is not free.</summary>
public static class CapabilityDetector
{
    static readonly string[] OfficeProgIds = ["Word.Application", "PowerPoint.Application", "Excel.Application"];

    public static Capabilities Detect() => new(
        Office: OfficeProgIds.All(IsProgIdRegistered),
        Ffmpeg: FindOnPath("ffmpeg.exe", Environment.GetEnvironmentVariable("PATH")),
        HeicWrite: CapabilityProbe.CanWrite("heic"),
        AvifWrite: CapabilityProbe.CanWrite("avif"),
        Ocr: false,
        BackgroundRemoval: false);

    /// <summary>True when <paramref name="fileName"/> exists in one of the folders of a PATH-style list.</summary>
    public static bool FindOnPath(string fileName, string? pathList)
    {
        if (string.IsNullOrEmpty(pathList)) return false;
        foreach (var raw in pathList.Split(Path.PathSeparator))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            try
            {
                if (File.Exists(Path.Combine(dir, fileName))) return true;
            }
            catch (ArgumentException) { /* malformed PATH entry */ }
        }
        return false;
    }

    static bool IsProgIdRegistered(string progId)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\CLSID");
            return key?.GetValue(null) is string clsid && clsid.Length > 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
