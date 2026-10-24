using System.Text.Json;
using Zest.Core.Catalog;
using Zest.Core.Convert;
using Zest.Core.Jobs;
using Zest.Core.Output;

namespace Zest.Core.Metadata;

/// <summary>
/// Container tags of audio and video (ffprobe <c>format_tags</c>): read with ffprobe, rewritten with ffmpeg stream copy,
/// so nothing is re-encoded.
/// </summary>
internal static class MediaMetadata
{
    const string Unreadable = "메타데이터를 읽을 수 없습니다.";
    const string SaveFailure = "메타데이터를 저장하지 못했습니다";
    const string Family = "media:";

    // ---- Read ----

    public static async Task<IReadOnlyList<MetadataTag>> ReadAsync(string path, CancellationToken token)
    {
        var tags = await ReadTagsAsync(path, token);
        var rows = new List<MetadataTag>();
        foreach (var (key, value) in tags)
        {
            var (group, label) = MetadataLabels.For("media", key);
            rows.Add(new($"{Family}{key}", group, label, value, Editable: true, Removable: true));
        }
        return rows;
    }

    /// <summary>Keys as ffprobe reports them (case-sensitive), values as they are.</summary>
    static async Task<List<KeyValuePair<string, string>>> ReadTagsAsync(string path, CancellationToken token)
    {
        var ffmpeg = FfmpegLocator.Locate();
        var ffprobe = ffmpeg is null ? null : FfmpegLocator.LocateProbe(ffmpeg);
        if (ffprobe is null) throw new ZestException(Unreadable);

        var json = await FfmpegProcess.CaptureAsync(ffprobe, ["-v", "error", "-show_entries", "format_tags", "-of", "json", path], token);
        if (string.IsNullOrWhiteSpace(json)) throw new ZestException(Unreadable);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("format", out var format)) throw new ZestException(Unreadable);
            var result = new List<KeyValuePair<string, string>>();
            if (format.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                foreach (var tag in tags.EnumerateObject())
                    result.Add(new(tag.Name, tag.Value.ValueKind == JsonValueKind.String ? tag.Value.GetString() ?? "" : tag.Value.ToString()));
            return result;
        }
        catch (JsonException)
        {
            throw new ZestException(Unreadable);
        }
    }

    // ---- Write ----

    public static async Task<string> WriteAsync(string source, MetadataEdit edit, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var ffmpeg = FfmpegLocator.Locate() ?? throw new ZestException($"{SaveFailure}: ffmpeg를 실행할 수 없습니다.");
        var ext = Path.GetExtension(source).TrimStart('.');
        var lower = ext.ToLowerInvariant();
        var existing = edit.RemoveAll ? [] : await ReadTagsAsync(source, ctx.Token);

        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", source, "-map", "0", "-c", "copy" };
        if (edit.RemoveAll)
        {
            args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1", "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact"]);
        }
        else
        {
            args.AddRange(["-map_metadata", "0"]);
            foreach (var (id, value) in edit.Changed)
                if (id.StartsWith(Family, StringComparison.Ordinal)) args.AddRange(["-metadata", $"{id[Family.Length..]}={value}"]);
            foreach (var id in edit.Removed)
                if (id.StartsWith(Family, StringComparison.Ordinal)) args.AddRange(["-metadata", $"{id[Family.Length..]}="]);
            if (edit.RemoveLocation)
                foreach (var (key, _) in existing)
                    if (MetadataLabels.IsLocationKey("media", key)) args.AddRange(["-metadata", $"{key}="]);
        }
        if (lower is "mp4" or "mov" or "m4a") args.AddRange(["-movflags", "+faststart+use_metadata_tags"]);

        var output = OutputNaming.ToolOutput(source, edit.RemoveAll ? "no metadata" : "edited", ext, ctx);
        args.Add(output);
        await FfmpegProcess.RunAsync(ffmpeg, args, null, ctx, SaveFailure);
        ctx.Report(1);
        return output;
    }
}
