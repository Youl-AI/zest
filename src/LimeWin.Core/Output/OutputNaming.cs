namespace LimeWin.Core.Output;

/// <summary>Output file naming. Results always land next to the source; existing files are never overwritten.</summary>
public static class OutputNaming
{
    public static string Stem(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>`stem.ext`, or `stem 2.ext`, `stem 3.ext`... when taken.</summary>
    public static string UniquePath(string directory, string stem, string ext)
    {
        var candidate = Path.Combine(directory, $"{stem}.{ext}");
        for (var n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
            candidate = Path.Combine(directory, $"{stem} {n}.{ext}");
        return candidate;
    }

    /// <summary>`name`, or `name 2`, `name 3`... when taken: the whole name is numbered (folders, names without an extension).</summary>
    public static string UniqueName(string directory, string name)
    {
        var candidate = Path.Combine(directory, name);
        for (var n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
            candidate = Path.Combine(directory, $"{name} {n}");
        return candidate;
    }

    /// <summary>The last part of a path for messages and lists (a trailing separator ignored); a drive or share root as a whole.</summary>
    public static string DisplayName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    public static string ConvertOutput(string source, string targetFormat) =>
        UniquePath(DirectoryOf(source), Stem(source), targetFormat);

    /// <summary>`name (tag).ext`, or `name (tag) 2.ext`... when taken.</summary>
    public static string ToolOutput(string source, string tag, string ext) =>
        UniquePath(DirectoryOf(source), $"{Stem(source)} ({tag})", ext);

    /// <summary>Creates a unique `name (pages)` (or `name (pages) 2`...) folder next to the source and returns its path.</summary>
    public static string CreatePagesFolder(string source)
    {
        var candidate = UniqueName(DirectoryOf(source), $"{Stem(source)} (pages)");
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    /// <summary>`stem 01.ext`; index is 1-based, zero-padded to the digit count of <paramref name="count"/> (min 1).</summary>
    public static string PageFileName(string stem, int index1, int count, string ext)
    {
        var width = Math.Max(1, count.ToString().Length);
        return $"{stem} {index1.ToString().PadLeft(width, '0')}.{ext}";
    }

    static string DirectoryOf(string source) =>
        Path.GetDirectoryName(Path.GetFullPath(source)) ?? throw new LimeException($"Cannot determine folder of '{source}'.");
}
