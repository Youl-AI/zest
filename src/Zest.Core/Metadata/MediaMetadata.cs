using System.Text.Json;
using Zest.Core.Catalog;
using Zest.Core.Convert;
using Zest.Core.Jobs;
using Zest.Core.Output;

namespace Zest.Core.Metadata;

/// <summary>
/// Container and stream tags of audio and video (ffprobe <c>format_tags</c>, <c>stream_tags</c>): read with ffprobe, rewritten
/// with ffmpeg stream copy, so nothing is re-encoded. Stream tags have ids <c>media:s{index}:{key}</c>.
/// </summary>
internal static class MediaMetadata
{
    const string Unreadable = "메타데이터를 읽을 수 없습니다.";
    const string SaveFailure = "메타데이터를 저장하지 못했습니다";
    const string Family = "media:";

    // ---- Read ----

    /// <summary>One container tag: <see cref="Stream"/> is null for the file's own (format) tags, else the stream's index.</summary>
    readonly record struct Tag(int? Stream, string Key, string Value);

    public static async Task<IReadOnlyList<MetadataTag>> ReadAsync(string path, CancellationToken token)
    {
        var (tags, _) = await ReadTagsAsync(path, token);
        var rows = new List<MetadataTag>();
        foreach (var (stream, key, value) in tags)
        {
            var (group, label) = MetadataLabels.For("media", key);
            var editable = !MetadataLabels.IsLocationKey("media", key); // like the image GPS rows: removed, never edited
            rows.Add(stream is { } i
                ? new($"{Family}s{i}:{key}", "미디어", $"{label} · 스트림 {i + 1}", value, editable, Removable: true)
                : new($"{Family}{key}", group, label, value, editable, Removable: true));
        }
        return rows;
    }

    /// <summary>
    /// Format tags, then each stream's tags (keys as ffprobe reports them, case-sensitive; values as they are), and every
    /// stream's codec type by index.
    /// </summary>
    static async Task<(List<Tag> Tags, List<string> StreamTypes)> ReadTagsAsync(string path, CancellationToken token)
    {
        var ffmpeg = FfmpegLocator.Locate();
        var ffprobe = ffmpeg is null ? null : FfmpegLocator.LocateProbe(ffmpeg);
        if (ffprobe is null) throw new ZestException(Unreadable);

        var json = await FfmpegProcess.CaptureAsync(ffprobe,
            ["-v", "error", "-show_entries", "format_tags:stream=index,codec_type:stream_tags", "-of", "json", path], token);
        if (string.IsNullOrWhiteSpace(json)) throw new ZestException(Unreadable);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("format", out var format)) throw new ZestException(Unreadable);
            var result = new List<Tag>();
            AddTags(format, null, result);
            var types = new List<string>();
            if (doc.RootElement.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
                foreach (var stream in streams.EnumerateArray())
                {
                    var index = stream.TryGetProperty("index", out var n) && n.TryGetInt32(out var i) ? i : types.Count;
                    types.Add(stream.TryGetProperty("codec_type", out var t) ? t.GetString() ?? "" : "");
                    AddTags(stream, index, result);
                }
            return (result, types);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new ZestException(Unreadable);
        }
    }

    static void AddTags(JsonElement owner, int? stream, List<Tag> into)
    {
        if (owner.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
            foreach (var tag in tags.EnumerateObject())
                into.Add(new(stream, tag.Name, tag.Value.ValueKind == JsonValueKind.String ? tag.Value.GetString() ?? "" : tag.Value.ToString()));
    }

    // ---- Write ----

    public static async Task<string> WriteAsync(string source, MetadataEdit edit, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var ffmpeg = FfmpegLocator.Locate() ?? throw new ZestException($"{SaveFailure}: ffmpeg를 실행할 수 없습니다.");
        var ext = Path.GetExtension(source).TrimStart('.');
        var lower = ext.ToLowerInvariant();
        var (existing, types) = edit.RemoveAll ? ([], []) : await ReadTagsAsync(source, ctx.Token);

        // Data streams (GoPro gpmd, iPhone mebx...) can carry GPS telemetry: a location or full wipe leaves them out
        // (decisions F3). Subtitle streams stay; telemetry inside them (DJI) is a known limit.
        var dropData = edit.RemoveLocation || edit.RemoveAll;
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", source, "-map", "0" };
        if (dropData) args.AddRange(["-map", "-0:d?"]);
        args.AddRange(["-c", "copy"]);
        if (edit.RemoveAll)
        {
            args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1", "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact"]);
        }
        else
        {
            // -metadata:s:N names an output stream: with data streams left out, the ones after them move up.
            int? OutputStream(int input) =>
                input >= types.Count ? input :
                !dropData ? input :
                types[input] == "data" ? null :
                types.Take(input).Count(t => t != "data");

            void Set(string id, string value)
            {
                if (!id.StartsWith(Family, StringComparison.Ordinal)) return;
                var rest = id[Family.Length..];
                var colon = rest.IndexOf(':');
                if (rest.StartsWith('s') && colon > 1 && int.TryParse(rest.AsSpan(1, colon - 1), out var input))
                {
                    if (OutputStream(input) is { } output) args.AddRange([$"-metadata:s:{output}", $"{rest[(colon + 1)..]}={value}"]);
                }
                else args.AddRange(["-metadata", $"{rest}={value}"]);
            }

            args.AddRange(["-map_metadata", "0"]);
            foreach (var (id, value) in edit.Changed) Set(id, value);
            foreach (var id in edit.Removed) Set(id, "");
            if (edit.RemoveLocation)
                foreach (var (stream, key, _) in existing)
                    if (MetadataLabels.IsLocationKey("media", key)) Set(stream is { } i ? $"{Family}s{i}:{key}" : Family + key, "");
        }
        // use_metadata_tags makes the mov muxer write mdta keys only, which Explorer, Apple Music and most players do not read:
        // mp4/m4a stay iTunes-style (ilst ©nam...) unless they hold a reverse-DNS key that only mdta can keep (decisions F2).
        if (lower == "mov" || lower is "mp4" or "m4a" && existing.Any(t => t.Stream is null && t.Key.Contains('.')))
            args.AddRange(["-movflags", "+faststart+use_metadata_tags"]);
        else if (lower is "mp4" or "m4a")
            args.AddRange(["-movflags", "+faststart"]);

        var output = OutputNaming.ToolOutput(source, edit.RemoveAll ? "no metadata" : "edited", ext, ctx);
        args.Add(output);
        await FfmpegProcess.RunAsync(ffmpeg, args, null, ctx, SaveFailure);
        ctx.Report(1);
        return output;
    }
}
