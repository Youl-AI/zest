using System.Runtime.InteropServices;
using Zest.Core.Jobs;

namespace Zest.Core.Output;

/// <summary>
/// Output file naming. Results always land next to the source; existing files are never overwritten.
/// Writers claim their name atomically where they choose it (<see cref="ReservePath"/> creates an empty placeholder with
/// <see cref="FileMode.CreateNew"/>, <see cref="ReserveFolder"/> creates the folder), so jobs running side by side can never
/// pick, overwrite or clean up the same name. The writer then writes over its own placeholder.
/// </summary>
public static class OutputNaming
{
    const int ErrorFileExists = 80, ErrorAlreadyExists = 183;

    public static string Stem(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>Claims `stem.ext`, or `stem 2.ext`, `stem 3.ext`... by creating it empty (never opening an existing file); returns the path.</summary>
    public static string ReservePath(string directory, string stem, string ext)
    {
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(directory, n == 1 ? $"{stem}.{ext}" : $"{stem} {n}.{ext}");
            if (Directory.Exists(candidate)) continue;
            try
            {
                using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                return candidate;
            }
            catch (IOException e) when (Win32Code(e) is ErrorFileExists or ErrorAlreadyExists) { /* taken: next number */ }
            catch (UnauthorizedAccessException) when (Directory.Exists(candidate)) { /* a folder appeared meanwhile */ }
        }
    }

    /// <summary>Creates `name`, or `name 2`, `name 3`... as a new folder (the whole name is numbered); returns its path.</summary>
    public static string ReserveFolder(string directory, string name)
    {
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(directory, n == 1 ? name : $"{name} {n}");
            if (TryCreateFolder(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Creates exactly this folder and returns true, or returns false when anything of that name already exists
    /// (<see cref="Directory.CreateDirectory(string)"/> would silently accept an existing folder). The parent must exist.
    /// </summary>
    public static bool TryCreateFolder(string path)
    {
        var full = Path.GetFullPath(path);
        if (CreateDirectoryW(Extended(full), IntPtr.Zero)) return true;
        var error = Marshal.GetLastPInvokeError();
        if (error is ErrorAlreadyExists or ErrorFileExists) return false;
        throw new IOException($"Could not create '{full}'.", Marshal.GetHRForLastWin32Error());
    }

    /// <summary>
    /// A name that is free right now (`stem.ext`, `stem 2.ext`...); nothing is created. Only for callers whose own write
    /// refuses an existing target (FileMover's moves); writers use <see cref="ReservePath"/>.
    /// </summary>
    public static string FreePath(string directory, string stem, string ext)
    {
        var candidate = Path.Combine(directory, $"{stem}.{ext}");
        for (var n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
            candidate = Path.Combine(directory, $"{stem} {n}.{ext}");
        return candidate;
    }

    /// <summary>`name`, or `name 2`, `name 3`... when taken: the whole name is numbered. Nothing is created; see <see cref="FreePath"/>.</summary>
    public static string FreeName(string directory, string name)
    {
        var candidate = Path.Combine(directory, name);
        for (var n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
            candidate = Path.Combine(directory, $"{name} {n}");
        return candidate;
    }

    /// <summary>The last part of a path for messages and lists (a trailing separator ignored); a drive or share root as a whole.</summary>
    public static string DisplayName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    /// <summary>Reserves `stem.<paramref name="targetFormat"/>` next to the source and tracks it on <paramref name="ctx"/>.</summary>
    public static string ConvertOutput(string source, string targetFormat, OperationContext ctx) =>
        Tracked(ctx, ReservePath(DirectoryOf(source), Stem(source), targetFormat));

    /// <summary>Reserves `name (tag).ext` (or `name (tag) 2.ext`...) next to the source and tracks it on <paramref name="ctx"/>.</summary>
    public static string ToolOutput(string source, string tag, string ext, OperationContext ctx) =>
        Tracked(ctx, ReservePath(DirectoryOf(source), $"{Stem(source)} ({tag})", ext));

    /// <summary>Creates a new `name (pages)` (or `name (pages) 2`...) folder next to the source, tracks it on <paramref name="ctx"/> and returns it.</summary>
    public static string CreatePagesFolder(string source, OperationContext ctx) =>
        Tracked(ctx, ReserveFolder(DirectoryOf(source), $"{Stem(source)} (pages)"));

    /// <summary>`stem 01.ext`; index is 1-based, zero-padded to the digit count of <paramref name="count"/> (min 1).</summary>
    public static string PageFileName(string stem, int index1, int count, string ext)
    {
        var width = Math.Max(1, count.ToString().Length);
        return $"{stem} {index1.ToString().PadLeft(width, '0')}.{ext}";
    }

    static string Tracked(OperationContext ctx, string reserved)
    {
        ctx.Track(reserved);
        return reserved;
    }

    static string DirectoryOf(string source) =>
        Path.GetDirectoryName(Path.GetFullPath(source)) ?? throw new ZestException($"Cannot determine folder of '{source}'.");

    /// <summary>The Win32 error code inside an HRESULT of the form 0x8007xxxx, else -1.</summary>
    static int Win32Code(Exception e) => (e.HResult & 0xFFFF0000) == 0x80070000 ? e.HResult & 0xFFFF : -1;

    /// <summary>A full path in the <c>\\?\</c> form when it is too long for the plain Win32 call.</summary>
    static string Extended(string full)
    {
        if (full.Length < 248 || full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);
}
