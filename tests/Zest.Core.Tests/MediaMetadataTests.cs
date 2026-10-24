using System.Diagnostics;
using System.Text.Json;
using Zest.Core.Catalog;
using Zest.Core.Convert;
using Zest.Core.Jobs;
using Zest.Core.Metadata;
using Zest.Core.Tests.Fixtures;

namespace Zest.Core.Tests;

[Trait("Category", "Ffmpeg")]
[Collection(ProcessCollection.Name)]
public class MediaMetadataTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static Task<IReadOnlyList<MetadataTag>> Read(string path) => MetadataReader.ReadAsync(path, Capabilities.All, CancellationToken.None);

    static string Probe(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(FfmpegLocator.LocateProbe(FfmpegLocator.Locate()!)!)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error" }.Concat(args).Append(file)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return text;
    }

    static Dictionary<string, string> FormatTags(string file)
    {
        using var doc = JsonDocument.Parse(Probe(file, "-show_entries", "format_tags", "-of", "json"));
        var result = new Dictionary<string, string>();
        if (doc.RootElement.TryGetProperty("format", out var f) && f.TryGetProperty("tags", out var tags))
            foreach (var t in tags.EnumerateObject()) result[t.Name] = t.Value.GetString() ?? "";
        return result;
    }

    static List<Dictionary<string, string>> StreamTags(string file)
    {
        using var doc = JsonDocument.Parse(Probe(file, "-show_entries", "stream_tags", "-of", "json"));
        var result = new List<Dictionary<string, string>>();
        foreach (var stream in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var tags = new Dictionary<string, string>();
            if (stream.TryGetProperty("tags", out var t))
                foreach (var tag in t.EnumerateObject()) tags[tag.Name] = tag.Value.GetString() ?? "";
            result.Add(tags);
        }
        return result;
    }

    static string[] StreamTypes(string file) =>
        Probe(file, "-show_entries", "stream=codec_type", "-of", "csv=p=0").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Fact]
    public async Task Read_ListsTitleAndArtist()
    {
        var src = FixtureFactory.TaggedMedia(Dir, "a.mp3", new Dictionary<string, string> { ["title"] = "바다", ["artist"] = "제스트" });

        var tags = await Read(src);

        var title = Assert.Single(tags, t => t.Id == "media:title");
        Assert.Equal(("미디어", "제목", "바다", true), (title.Group, title.Label, title.Value, title.Editable));
        Assert.Equal("제스트", Assert.Single(tags, t => t.Id == "media:artist").Value);
    }

    [Fact]
    public async Task Edit_Title_RoundTrips()
    {
        var src = FixtureFactory.TaggedMedia(Dir, "a.mp3", new Dictionary<string, string> { ["title"] = "바다", ["artist"] = "제스트" });
        var edit = new MetadataEdit(new Dictionary<string, string> { ["media:title"] = "파도" }, new HashSet<string>(), RemoveLocation: false, RemoveAll: false);

        var output = await MetadataWriter.ApplyAsync(src, edit, Ctx);

        Assert.EndsWith(" (edited).mp3", output);
        var tags = await Read(output);
        Assert.Equal("파도", Assert.Single(tags, t => t.Id == "media:title").Value);
        Assert.Equal("제스트", Assert.Single(tags, t => t.Id == "media:artist").Value);
    }

    [Fact]
    public async Task RemoveLocation_DropsQuickTimeLocation_KeepsStreams()
    {
        var src = FixtureFactory.TaggedMedia(Dir, "clip.mov", new Dictionary<string, string>
        {
            ["com.apple.quicktime.location.ISO6709"] = "+33.4996+126.5312/",
            ["title"] = "x",
        });
        Assert.Contains(FormatTags(src).Keys, k => k.Contains("location", StringComparison.OrdinalIgnoreCase));

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.LocationOnly, Ctx);

        var after = FormatTags(output);
        Assert.DoesNotContain(after.Keys, k => k.Contains("location", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("x", after["title"]);
        Assert.Equal(StreamTypes(src), StreamTypes(output));
    }

    [Fact]
    public async Task M4a_EditTitle_StaysItunesStyle()
    {
        var src = FixtureFactory.TaggedMedia(Dir, "a.m4a", new Dictionary<string, string> { ["title"] = "바다", ["artist"] = "제스트" });
        var edit = new MetadataEdit(new Dictionary<string, string> { ["media:title"] = "파도" }, new HashSet<string>(), RemoveLocation: false, RemoveAll: false);

        var output = await MetadataWriter.ApplyAsync(src, edit, Ctx);

        var bytes = File.ReadAllBytes(output);
        Assert.True(bytes.AsSpan().IndexOf((byte[])[0xA9, 0x6E, 0x61, 0x6D]) >= 0); // ©nam, what Explorer and players read
        Assert.True(bytes.AsSpan().IndexOf("mdta"u8) < 0);
        Assert.Equal("파도", FormatTags(output)["title"]);
        Assert.Equal("제스트", FormatTags(output)["artist"]);
    }

    [Fact]
    public async Task RemoveLocation_DropsStreamLevelLocation()
    {
        var src = FixtureFactory.TaggedMedia(Dir, "clip.mkv", new Dictionary<string, string> { ["title"] = "x" },
            audioStreamTags: new Dictionary<string, string> { ["location"] = "+33.4996+126.5312/" });
        Assert.Contains(StreamTags(src).SelectMany(t => t.Keys), k => k.Contains("location", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(await Read(src), t => t.Id.StartsWith("media:s", StringComparison.Ordinal) && t.Id.Contains("location", StringComparison.OrdinalIgnoreCase));

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.LocationOnly, Ctx);

        Assert.DoesNotContain(StreamTags(output).SelectMany(t => t.Keys), k => k.Contains("location", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(FormatTags(output).Keys, k => k.Contains("location", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("x", FormatTags(output)["title"]);
        Assert.Equal(StreamTypes(src), StreamTypes(output));
    }

    [Fact]
    public async Task RemoveAll_LeavesNoTags()
    {
        var src = FixtureFactory.TaggedMedia(Dir, "clip.mp4", new Dictionary<string, string> { ["title"] = "x", ["artist"] = "y" });

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.Everything, Ctx);

        Assert.EndsWith(" (no metadata).mp4", output);
        var left = FormatTags(output).Keys.Except(["major_brand", "minor_version", "compatible_brands"]);
        Assert.Empty(left);
    }
}
