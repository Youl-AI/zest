using System.Runtime.InteropServices;
using System.Text;

namespace LimeWin.Core.Input;

/// <summary>
/// Decides whether a dragged file list is something the user owns. Archive tools (Bandizip, 7-Zip, WinRAR, the shell's zip
/// folder) hand out CF_HDROP paths of copies they extracted under the Windows temp folder; Bandizip even deletes the entry
/// from the archive when the target accepts a drop made with Shift held. Such drags are refused, never converted.
/// </summary>
public static class DropSourcePolicy
{
    const char Sep = (char)92;

    /// <summary>False if the list is empty, any path is empty or relative, or any path lies inside <paramref name="tempRoot"/>.</summary>
    public static bool IsRealUserFiles(IReadOnlyList<string> paths, string tempRoot)
    {
        if (paths.Count == 0 || string.IsNullOrWhiteSpace(tempRoot)) return false;
        var temp = Normalize(tempRoot);
        if (temp is null) return false;
        var prefix = temp.TrimEnd(Sep) + "\\";
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p)) return false;
            var full = Normalize(p);
            if (full is null) return false;
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>Full path with 8.3 segments expanded: the longest existing ancestor goes through GetLongPathName, the rest is kept as is.</summary>
    static string? Normalize(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        full = full.TrimEnd(Sep);

        var existing = full;
        var tail = "";
        while (existing.Length > 3 && !Path.Exists(existing))
        {
            tail = existing[(existing.LastIndexOf(Sep) + 1)..] + (tail.Length == 0 ? "" : "\\" + tail);
            existing = existing[..existing.LastIndexOf(Sep)];
        }
        var sb = new StringBuilder(1024);
        var len = GetLongPathName(existing, sb, sb.Capacity);
        var resolved = len > 0 && len < sb.Capacity ? sb.ToString() : existing;
        return tail.Length == 0 ? resolved : resolved.TrimEnd(Sep) + "\\" + tail;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetLongPathName(string shortPath, StringBuilder longPath, int bufferLength);
}
