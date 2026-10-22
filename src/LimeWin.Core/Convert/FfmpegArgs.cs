namespace LimeWin.Core.Convert;

/// <summary>The ffmpeg argument table for audio, video and GIF targets. Pure; elements go to ProcessStartInfo.ArgumentList.</summary>
public static class FfmpegArgs
{
    const string GifFilter =
        "[0:v:0]fps=15,scale='min(960,iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a";

    static readonly Dictionary<string, string[]> Targets = new()
    {
        ["mp3"] = ["-vn", "-c:a", "libmp3lame", "-b:a", "256k"],
        ["m4a"] = ["-vn", "-c:a", "aac", "-b:a", "256k"],
        ["wav"] = ["-vn", "-c:a", "pcm_s16le"],
        ["aiff"] = ["-vn", "-c:a", "pcm_s16be"],
        ["flac"] = ["-vn", "-c:a", "flac", "-compression_level", "6"],
        ["ogg"] = ["-vn", "-c:a", "libvorbis", "-q:a", "6"],
        ["opus"] = ["-vn", "-c:a", "libopus", "-b:a", "192k"],
        ["wma"] = ["-vn", "-c:a", "wmav2", "-b:a", "192k"],
        ["mp4"] = ["-c:v", "libx264", "-crf", "20", "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "256k", "-movflags", "+faststart"],
        ["mov"] = ["-c:v", "libx264", "-crf", "17", "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "256k"],
        ["mkv"] = ["-c:v", "libx264", "-crf", "20", "-preset", "medium", "-c:a", "aac", "-b:a", "256k"],
        ["avi"] = ["-c:v", "mpeg4", "-q:v", "3", "-c:a", "libmp3lame", "-b:a", "256k"],
        ["wmv"] = ["-c:v", "wmv2", "-q:v", "3", "-c:a", "wmav2", "-b:a", "192k"],
        ["gif"] = ["-filter_complex", GifFilter, "-an"],
    };

    public static IEnumerable<string> AllTargets => Targets.Keys;

    public static bool IsTarget(string target) => Targets.ContainsKey(target);

    public static IReadOnlyList<string> For(string source, string sourceFormat, string target, string output)
    {
        if (!Targets.TryGetValue(target, out var tail))
            throw new LimeException("이 변환은 지원하지 않습니다.");
        return ["-hide_banner", "-nostdin", "-y", "-i", source, "-map_metadata", "0", .. tail, output];
    }

    /// <summary>Encoders the target needs (read off the table), so a missing one can drop the target at runtime.</summary>
    public static IReadOnlyList<string> EncodersFor(string target)
    {
        if (!Targets.TryGetValue(target, out var tail)) return [];
        var list = new List<string>();
        for (var i = 0; i < tail.Length - 1; i++)
            if (tail[i] is "-c:a" or "-c:v") list.Add(tail[i + 1]);
        return list;
    }
}
