using Zest.Core.Convert;

namespace Zest.Core.Tests;

public class CapabilityDetectorTests
{
    // Office and Ffmpeg depend on the machine, so only the probe-backed and not-yet-built engines are asserted.
    [Fact]
    public void Detect_UsesProbesForHeicAvifOcrAndBackgroundRemoval()
    {
        var caps = CapabilityDetector.Detect();

        Assert.Equal(CapabilityProbe.CanWrite("heic"), caps.HeicWrite);
        Assert.Equal(CapabilityProbe.CanWrite("avif"), caps.AvifWrite);
        Assert.Equal(Zest.Core.Tools.OcrService.IsAvailable, caps.Ocr);
        Assert.Equal(Zest.Core.Tools.BackgroundRemover.IsAvailable, caps.BackgroundRemoval);
        Assert.True(caps.BackgroundRemoval); // Windows ML ships with the app, so it loads on any supported Windows
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
