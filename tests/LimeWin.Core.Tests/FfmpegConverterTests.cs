using System.Diagnostics;
using System.Security.Cryptography;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Tests.Fixtures;

namespace LimeWin.Core.Tests;

public class FfmpegArgsTests
{
    [Fact] public void Args_Mp4_HasFaststartAndYuv420p() { var a = FfmpegArgs.For("in.mov", "mov", "mp4", "out.mp4"); Assert.Contains("+faststart", a); Assert.Contains("yuv420p", a); Assert.Equal("out.mp4", a[^1]); }

    [Fact] public void Args_Gif_Uses15FpsAnd960Cap() => Assert.Contains(FfmpegArgs.For("a.mp4", "mp4", "gif", "a.gif"), x => x.Contains("fps=15") && x.Contains("min(960,iw)"));

    [Fact]
    public void Args_KoreanPath_PassedVerbatim()
    {
        var a = FfmpegArgs.For(@"C:\자료\영상 (1).mp4", "mp4", "mp3", "o.mp3").ToList();
        Assert.Equal(@"C:\자료\영상 (1).mp4", a[a.IndexOf("-i") + 1]);
    }

    [Theory]
    [InlineData("mp3", "libmp3lame")]
    [InlineData("ogg", "libvorbis")]
    [InlineData("mov", "libx264")]
    [InlineData("wmv", "wmv2")]
    public void Args_CommonHead_AndCodec(string target, string codec)
    {
        var a = FfmpegArgs.For("in.x", "x", target, "out." + target);
        Assert.Equal(new[] { "-hide_banner", "-nostdin", "-y", "-i", "in.x", "-map_metadata", "0" }, a.Take(7));
        Assert.Contains(codec, a);
        Assert.Contains(codec, FfmpegArgs.EncodersFor(target));
    }

    [Fact] public void Args_Mov_UsesCrf17_Mp4Uses20() { Assert.Contains("17", FfmpegArgs.For("a", "mp4", "mov", "b")); Assert.Contains("20", FfmpegArgs.For("a", "mov", "mp4", "b")); }

    [Theory]
    [InlineData("mp4")]
    [InlineData("mov")]
    [InlineData("mkv")]
    public void Args_X264Targets_RoundToEvenDimensions(string target)
    {
        var a = FfmpegArgs.For("in.gif", "gif", target, "out." + target).ToList();
        Assert.Equal("scale=trunc(iw/2)*2:trunc(ih/2)*2", a[a.IndexOf("-vf") + 1]);
    }

    [Fact]
    public void UnknownTarget_Throws() => Assert.Throws<LimeException>(() => FfmpegArgs.For("a.mp4", "mp4", "webm", "a.webm"));

    [Fact]
    public void Catalog_DropsTargetsListedAsUnavailable()
    {
        var caps = Capabilities.All with { UnavailableTargets = new HashSet<string> { "ogg", "wmv" } };
        Assert.DoesNotContain("ogg", FormatCatalog.TargetsFor("mp3", caps));
        Assert.Contains("opus", FormatCatalog.TargetsFor("mp3", caps));
        Assert.DoesNotContain("wmv", FormatCatalog.TargetsFor("mp4", caps));
        Assert.Contains("ogg", FormatCatalog.TargetsFor("mp3", Capabilities.All));
    }

    [Fact]
    public void Handles_FollowsCatalogCategories()
    {
        var c = new FfmpegConverter("ffmpeg.exe");
        Assert.True(c.Handles("mp4", "gif"));
        Assert.True(c.Handles("mp4", "mp3"));
        Assert.True(c.Handles("gif", "mp4"));
        Assert.True(c.Handles("webm", "mkv"));
        Assert.True(c.Handles("flac", "ogg"));
        Assert.False(c.Handles("mp3", "mp4")); // audio cannot become video
        Assert.False(c.Handles("mp4", "mp4"));
        Assert.False(c.Handles("png", "mp4"));
        Assert.False(c.Handles("mp4", "webm")); // input only
    }
}

[Trait("Category", "Ffmpeg")]
public class FfmpegConverterTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static readonly string FfmpegPath = FfmpegLocator.Locate() ?? throw new InvalidOperationException("ffmpeg is not on PATH.");
    static ConversionRouter Router => new([new FfmpegConverter(FfmpegPath)]);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static readonly string[] Audio = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma"];
    static readonly string[] VideoSources = ["mp4", "mov", "mkv", "avi", "wmv", "webm", "gif"];
    static readonly string[] VideoTargets = ["mp4", "mov", "mkv", "avi", "wmv", "gif", "mp3"];

    public static IEnumerable<object[]> AudioVideoRoutes()
    {
        foreach (var from in Audio)
            foreach (var to in Audio.Where(t => t != from))
                yield return [from, to];
        foreach (var from in VideoSources)
            foreach (var to in VideoTargets.Where(t => t != from))
            {
                if (from == "gif" && to is "gif" or "mp3") continue; // not in the catalog for GIF sources
                yield return [from, to];
            }
    }

    static string[] StreamTypes(string file)
    {
        var psi = new ProcessStartInfo(FfmpegLocator.LocateProbe(FfmpegPath)!)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error", "-show_entries", "stream=codec_type", "-of", "csv=p=0", file }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [Theory, MemberData(nameof(AudioVideoRoutes))]
    public async Task Converts_AndFfprobeReadsResult(string from, string to)
    {
        var src = FixtureFactory.Media(Dir, $"영상 (1).{from}");
        var before = SHA256.HashData(File.ReadAllBytes(src));

        var outs = await Router.ConvertAsync(src, to, new OperationContext(CancellationToken.None));

        var output = Assert.Single(outs);
        Assert.Equal(Path.Combine(Dir, $"영상 (1).{to}"), output);
        var streams = StreamTypes(output);
        var isAudioTarget = Audio.Contains(to);
        if (isAudioTarget)
        {
            Assert.Contains("audio", streams);
            Assert.DoesNotContain("video", streams);
        }
        else
        {
            Assert.Contains("video", streams);
            if (to != "gif" && from != "gif") Assert.Contains("audio", streams);
        }
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(src)));
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("avi")]
    public async Task OddSizedSource_ToMp4_HasEvenDimensions(string from)
    {
        var src = FixtureFactory.Media(Dir, $"odd.{from}", 2, 321, 241);
        var output = Assert.Single(await Router.ConvertAsync(src, "mp4", new OperationContext(CancellationToken.None)));

        var psi = new ProcessStartInfo(FfmpegLocator.LocateProbe(FfmpegPath)!)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", output }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var size = p.StandardOutput.ReadToEnd().Trim().Split(',').Select(int.Parse).ToArray();
        p.WaitForExit();
        Assert.Equal(new[] { 320, 240 }, size);
    }

    [Fact]
    public async Task Progress_ReachesOne_AndIncreases()
    {
        var src = FixtureFactory.Media(Dir, "p.mp4", 10);
        var seen = new List<double>();
        var progress = new SyncProgress(seen.Add);
        await Router.ConvertAsync(src, "mkv", new OperationContext(CancellationToken.None, progress));
        Assert.Equal(1.0, seen[^1]);
        Assert.True(seen.Count > 1, "expected intermediate progress");
        Assert.Equal(seen.Order(), seen);
    }

    sealed class SyncProgress(Action<double> onReport) : IProgress<double>
    {
        public void Report(double value) { lock (this) onReport(value); }
    }

    [Fact]
    public async Task Ffmpeg_Cancelled_KillsProcessAndRemovesOutput()
    {
        var src = FixtureFactory.Media(Dir, "long.mp4", 120);
        var before = Process.GetProcessesByName("ffmpeg").Select(p => p.Id).ToHashSet();
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(f => { if (f > 0.1) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Router.ConvertAsync(src, "mkv", new OperationContext(cts.Token, progress)));

        Assert.False(File.Exists(Path.Combine(Dir, "long.mkv")));
        Assert.Equal(new[] { "long.mp4" }, Directory.GetFiles(Dir).Select(Path.GetFileName));
        var leftover = Process.GetProcessesByName("ffmpeg").Where(p => !before.Contains(p.Id)).ToList();
        Assert.Empty(leftover);
    }

    [Fact]
    public async Task CorruptSource_ThrowsLimeException_WithMessage_AndLeavesNothing()
    {
        var src = Path.Combine(Dir, "깨진.mp4");
        File.WriteAllText(src, "not a video");
        var ex = await Assert.ThrowsAsync<LimeException>(() =>
            Router.ConvertAsync(src, "mkv", new OperationContext(CancellationToken.None)));
        Assert.StartsWith("변환 실패: ", ex.Message);
        Assert.Equal(new[] { "깨진.mp4" }, Directory.GetFiles(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Detector_ListsOnlyTargetsWhoseEncodersExist()
    {
        var missing = FfmpegLocator.UnavailableTargets(FfmpegPath);
        // gyan.dev full build 8.1.2 has every encoder in the table.
        Assert.Empty(missing);
        Assert.Empty(FfmpegLocator.UnavailableTargets(Path.Combine(Dir, "nope.exe"))); // unreadable list excludes nothing
    }
}
