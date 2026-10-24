using Zest.Core.Output;

namespace Zest.Core.Wheel;

/// <summary>One dragged path as the hub sees it: a folder (never sized), or a file and its length when it could be read.</summary>
public readonly record struct HubEntry(bool IsFolder, long? Bytes)
{
    /// <summary>
    /// Reads one path from disk. Can block for a long time on an offline network share, so callers run it off the UI thread.
    /// A path that is neither a readable file nor a folder counts as a file of unknown size.
    /// </summary>
    public static HubEntry Read(string path)
    {
        try
        {
            if (Directory.Exists(path)) return new(true, null);
            return new(false, new FileInfo(path).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return new(false, null);
        }
    }
}

/// <summary>What the wheel hub shows with no petal hovered: the dragged files' size and counts.</summary>
public static class HubSummary
{
    /// <summary>
    /// One file: its size ("5.6 MB"). Several files: "3개 · 12.4 MB". Any folder: "파일 2개 · 폴더 1개" or "폴더 1개" (folders are
    /// never sized, too slow). A size that could not be read is left out of the total; with none readable only the count
    /// is shown ("3개"), and a single unreadable file shows nothing ("").
    /// </summary>
    public static string For(IReadOnlyList<HubEntry> entries)
    {
        var folders = entries.Count(e => e.IsFolder);
        var files = entries.Count - folders;
        if (folders > 0) return files == 0 ? $"폴더 {folders}개" : $"파일 {files}개 · 폴더 {folders}개";

        var known = entries.Where(e => e.Bytes is not null).Select(e => e.Bytes!.Value).ToList();
        if (files == 1) return known.Count == 1 ? SizeText.Format(known[0]) : "";
        if (files == 0) return "";
        return known.Count == 0 ? $"{files}개" : $"{files}개 · {SizeText.Format(known.Sum())}";
    }
}
