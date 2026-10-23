using LimeWin.Core.Convert;

namespace LimeWin.Core.Tests;

public class CapabilityDetectorTests
{
    // Office and Ffmpeg depend on the machine, so only the probe-backed and not-yet-built engines are asserted.
    [Fact]
    public void Detect_UsesProbeForHeicAndAvif_AndLeavesLaterEnginesOff()
    {
        var caps = CapabilityDetector.Detect();

        Assert.Equal(CapabilityProbe.CanWrite("heic"), caps.HeicWrite);
        Assert.Equal(CapabilityProbe.CanWrite("avif"), caps.AvifWrite);
        Assert.Equal(LimeWin.Core.Tools.OcrService.IsAvailable, caps.Ocr);
        Assert.False(caps.BackgroundRemoval);
    }

    [Fact]
    public void FindOnPath_FindsFileInListedFolder_AndIgnoresMissingOrBlankEntries()
    {
        var dir = TestFiles.TempDir();
        try
        {
            var exe = Path.Combine(dir, "tool.exe");
            File.WriteAllBytes(exe, []);
            var path = string.Join(Path.PathSeparator, "", "  ", Path.Combine(dir, "missing"), $"\"{dir}\"");

            Assert.True(CapabilityDetector.FindOnPath("tool.exe", path));
            Assert.False(CapabilityDetector.FindOnPath("other.exe", path));
            Assert.False(CapabilityDetector.FindOnPath("tool.exe", null));
        }
        finally { Directory.Delete(dir, true); }
    }
}
