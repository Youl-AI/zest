using LimeWin.Core.Catalog;
using Microsoft.Win32;

namespace LimeWin.Core.Convert;

/// <summary>Works out which optional engines this machine has. Call once at startup; probing is not free.</summary>
public static class CapabilityDetector
{
    static readonly string[] OfficeProgIds = ["Word.Application", "PowerPoint.Application", "Excel.Application"];

    public static Capabilities Detect()
    {
        var ffmpeg = FfmpegLocator.Locate();
        return new(
            Office: OfficeProgIds.All(IsProgIdRegistered),
            Ffmpeg: ffmpeg is not null,
            HeicWrite: CapabilityProbe.CanWrite("heic"),
            AvifWrite: CapabilityProbe.CanWrite("avif"),
            Ocr: Tools.OcrService.IsAvailable,
            BackgroundRemoval: Tools.BackgroundRemover.IsAvailable, // the model is downloaded on first use
            UnavailableTargets: ffmpeg is null ? null : FfmpegLocator.UnavailableTargets(ffmpeg));
    }

    /// <summary>True when <paramref name="fileName"/> exists in one of the folders of a PATH-style list.</summary>
    public static bool FindOnPath(string fileName, string? pathList) => PathLookup.Find(fileName, pathList) is not null;

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
