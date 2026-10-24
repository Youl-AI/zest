using System.Runtime.InteropServices;
using System.Text;

namespace Zest.Core.Office;

/// <summary>
/// The private <c>%TEMP%\LimeWin\&lt;guid&gt;</c> folder one Office conversion reads and writes in (<c>in\</c>, <c>out\</c>),
/// deleted on Dispose. Office only ever opens a private copy of the source: opening the user's path would hand back their
/// own document if it is already open in that instance, and SaveAs + Close would then take it from them.
/// </summary>
public sealed class OfficeWorkFolder : IDisposable
{
    readonly string _root;

    OfficeWorkFolder(string root)
    {
        _root = root;
        In = Directory.CreateDirectory(Path.Combine(root, "in")).FullName;
        Out = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
    }

    public string In { get; }
    public string Out { get; }

    /// <summary><c>%TEMP%\LimeWin</c>: every work folder lives directly inside it.</summary>
    public static string RootFolder => Path.Combine(Path.GetTempPath(), "LimeWin");

    public static OfficeWorkFolder Create() =>
        new(Directory.CreateDirectory(Path.Combine(RootFolder, Guid.NewGuid().ToString("N"))).FullName);

    /// <summary>
    /// Deletes work folders an earlier run left behind (a crash or a kill skips Dispose): folders directly in
    /// <see cref="RootFolder"/> named like ours (32 hex digits) and untouched for over an hour. Run at startup, off the UI thread.
    /// </summary>
    public static void SweepStale() => SweepStale(RootFolder, DateTime.UtcNow, TimeSpan.FromHours(1));

    /// <summary>Best effort: a folder that cannot be deleted (in use, no access) is left for the next start.</summary>
    internal static void SweepStale(string root, DateTime nowUtc, TimeSpan olderThan)
    {
        string[] folders;
        try { folders = Directory.GetDirectories(root); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; } // no root yet

        foreach (var folder in folders)
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) continue;
            try
            {
                var touched = Max(Directory.GetCreationTimeUtc(folder), Directory.GetLastWriteTimeUtc(folder));
                if (nowUtc - touched > olderThan) Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    /// <summary>A fresh <c>in\&lt;guid&gt;&lt;extension&gt;</c> path (extension with its dot).</summary>
    public string NewInputPath(string extension) => Path.Combine(In, $"{Guid.NewGuid():N}{extension}");

    /// <summary>Copies the source to a fresh input path with <paramref name="extension"/> and returns it.</summary>
    public string CopySource(string source, string extension)
    {
        var input = NewInputPath(extension);
        try { File.Copy(source, input); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ZestException("원본 파일을 열 수 없습니다.");
        }
        return input;
    }

    /// <summary>
    /// A saved web page keeps its pictures in a sibling <c>&lt;stem&gt;_files</c> folder that its links name relatively;
    /// copying that folder into <see cref="In"/> lets Word, which reads only the private copy, find them.
    /// </summary>
    public void CopyCompanionFiles(string source)
    {
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, Path.GetFileNameWithoutExtension(source) + "_files");
        if (!Directory.Exists(folder)) return;
        try { CopyTree(folder, Path.Combine(In, Path.GetFileName(folder))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ZestException("원본 파일을 열 수 없습니다.");
        }
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from))
            if (!File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint)) // never follow a link (a junction could loop)
                CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// <summary>Office opens a 0-byte file as a blank document; converting nothing is not a result.</summary>
    public static void EnsureNotEmpty(string source)
    {
        long length;
        try { length = new FileInfo(source).Length; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ZestException("원본 파일을 열 수 없습니다.");
        }
        if (length == 0) throw new ZestException("빈 파일은 변환할 수 없습니다.");
    }

    /// <summary>
    /// Whether an open document's <c>FullName</c> is one of our private copies (<paramref name="ours"/>: the input and the
    /// saved path). Matched on the whole path, never the bare name, so a user's document that shares the name is never
    /// touched. 8.3 and long forms of the same path match.
    /// </summary>
    public static bool IsOurCopy(string? fullName, params string?[] ours)
    {
        if (string.IsNullOrEmpty(fullName) || !Path.IsPathFullyQualified(fullName)) return false;
        foreach (var path in ours)
        {
            if (string.IsNullOrEmpty(path)) continue;
            if (SamePath(fullName, path) || SamePath(LongForm(fullName), LongForm(path))) return true;
        }
        return false;
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The path with 8.3 segments expanded when it exists (else unchanged).</summary>
    static string LongForm(string path)
    {
        var sb = new StringBuilder(1024);
        var len = GetLongPathName(path, sb, sb.Capacity);
        return len > 0 && len < sb.Capacity ? sb.ToString() : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetLongPathName(string shortPath, StringBuilder longPath, int bufferLength);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* temp leftovers are harmless */ }
    }
}
