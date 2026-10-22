using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LimeWin.Core.Convert;

/// <summary>Finds ffmpeg/ffprobe and asks ffmpeg which encoders it has. The one place for ffmpeg discovery.</summary>
public static partial class FfmpegLocator
{
    /// <summary>Full path of ffmpeg.exe in the first PATH folder that has it, or null.</summary>
    public static string? Locate(string? pathList = null) =>
        Find("ffmpeg.exe", pathList ?? Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// ffprobe.exe next to <paramref name="ffmpegPath"/>, else next to its link target (WinGet "Links" holds symlinks),
    /// else from PATH, else null.
    /// </summary>
    public static string? LocateProbe(string ffmpegPath)
    {
        var dirs = new List<string?> { Path.GetDirectoryName(ffmpegPath) };
        try
        {
            if (File.ResolveLinkTarget(ffmpegPath, returnFinalTarget: true) is { } target)
                dirs.Add(Path.GetDirectoryName(target.FullName));
        }
        catch (IOException) { /* not a link, or unreadable */ }
        foreach (var dir in dirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir, "ffprobe.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return Find("ffprobe.exe", Environment.GetEnvironmentVariable("PATH"));
    }

    static string? Find(string fileName, string? pathList)
    {
        if (string.IsNullOrEmpty(pathList)) return null;
        foreach (var raw in pathList.Split(Path.PathSeparator))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* malformed PATH entry */ }
        }
        return null;
    }

    /// <summary>Audio/video/GIF targets whose encoders this ffmpeg lacks; empty when the encoder list cannot be read.</summary>
    public static IReadOnlySet<string> UnavailableTargets(string ffmpegPath)
    {
        var encoders = Encoders(ffmpegPath);
        if (encoders is null) return new HashSet<string>();
        return FfmpegArgs.AllTargets
            .Where(t => FfmpegArgs.EncodersFor(t).Any(e => !encoders.Contains(e)))
            .ToHashSet();
    }

    static HashSet<string>? Encoders(string ffmpegPath)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpegPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-encoders");
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            var text = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(10_000)) { p.Kill(true); return null; }
            err.Wait();
            var set = new HashSet<string>();
            foreach (Match m in EncoderLine().Matches(text)) set.Add(m.Groups[1].Value);
            return set.Count == 0 ? null : set;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^\s[VAS][A-Z.]{5}\s+(\S+)", RegexOptions.Multiline)]
    private static partial Regex EncoderLine();
}
