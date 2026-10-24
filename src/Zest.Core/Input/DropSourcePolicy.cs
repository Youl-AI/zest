using System.Runtime.InteropServices;
using System.Text;

namespace Zest.Core.Input;

/// <summary>
/// Decides whether a dragged file list is something the user owns. Archive tools (Bandizip, 7-Zip, WinRAR, the shell's zip
/// folder) hand out CF_HDROP paths of copies they extracted under the Windows temp folder; Bandizip even deletes the entry
/// from the archive when the target accepts a drop made with Shift held. Such drags are refused, never converted.
/// </summary>
public static class DropSourcePolicy
{
    const char Sep = (char)92;

    /// <summary>False if the list is empty, any path is empty or relative, or any path lies inside <paramref name="tempRoot"/>.</summary>
    public static bool IsRealUserFiles(IReadOnlyList<string> paths, string tempRoot) =>
        IsRealUserFiles(paths, tempRoot, Path.Exists, RealLongPath);

    /// <summary>Test seam: <paramref name="exists"/> and <paramref name="longPath"/> are the only file-system probes.</summary>
    internal static bool IsRealUserFiles(IReadOnlyList<string> paths, string tempRoot, Func<string, bool> exists, Func<string, string?> longPath)
    {
        if (paths.Count == 0 || string.IsNullOrWhiteSpace(tempRoot)) return false;
        var temp = Full(tempRoot);
        if (temp is null) return false;
        var tempRootDir = Path.GetPathRoot(temp) ?? "";
        string? tempExpanded = null; // expanded at most once per call
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p)) return false;
            var full = Full(p);
            if (full is null) return false;
            // Another drive or a UNC share cannot be inside the local temp folder: no file-system access for those.
            if (!string.Equals(Path.GetPathRoot(full), tempRootDir, StringComparison.OrdinalIgnoreCase)) continue;
            // Only names containing '~' can be 8.3 aliases; without one the plain full-path prefix test is exact.
            var t = temp;
            if (full.Contains(Tilde) || temp.Contains(Tilde))
            {
                full = ExpandShort(full, exists, longPath);
                t = tempExpanded ??= ExpandShort(temp, exists, longPath);
            }
            if (full.StartsWith(t.TrimEnd(Sep) + Sep, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    const char Tilde = '~';

    /// <summary>Full path without a trailing separator and without a leading <c>\\?\</c> / <c>\\?\UNC\</c> prefix.</summary>
    static string? Full(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        var dev = new string(Sep, 2) + "?" + Sep;
        if (full.StartsWith(dev + "UNC" + Sep, StringComparison.OrdinalIgnoreCase)) full = new string(Sep, 2) + full[(dev.Length + 4)..];
        else if (full.StartsWith(dev, StringComparison.Ordinal)) full = full[dev.Length..];
        return full.Length > 3 ? full.TrimEnd(Sep) : full;
    }

    /// <summary>8.3 segments expanded: the longest existing ancestor goes through GetLongPathName, the rest is kept as is.</summary>
    static string ExpandShort(string full, Func<string, bool> exists, Func<string, string?> longPath)
    {
        var existing = full;
        var tail = "";
        while (existing.Length > 3 && !exists(existing))
        {
            tail = existing[(existing.LastIndexOf(Sep) + 1)..] + (tail.Length == 0 ? "" : Sep + tail);
            existing = existing[..existing.LastIndexOf(Sep)];
        }
        var resolved = longPath(existing) ?? existing;
        return tail.Length == 0 ? resolved : resolved.TrimEnd(Sep) + Sep + tail;
    }

    static string? RealLongPath(string path)
    {
        var sb = new StringBuilder(1024);
        var len = GetLongPathName(path, sb, sb.Capacity);
        return len > 0 && len < sb.Capacity ? sb.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetLongPathName(string shortPath, StringBuilder longPath, int bufferLength);
}
