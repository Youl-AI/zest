namespace LimeWin.Core;

/// <summary>Finds a program the way a PATH search does (ffmpeg, ffprobe, wt.exe, ...).</summary>
public static class PathLookup
{
    /// <summary>Full path of <paramref name="fileName"/> in the first folder of a PATH-style list that has it, or null.</summary>
    public static string? Find(string fileName, string? pathList)
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
}
