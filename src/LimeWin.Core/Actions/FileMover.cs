using LimeWin.Core.Jobs;
using LimeWin.Core.Output;

namespace LimeWin.Core.Actions;

/// <param name="Moved">Where each moved item ended up.</param>
/// <param name="Failures">One Korean line per item that could not be moved.</param>
public sealed record MoveResult(IReadOnlyList<string> Moved, IReadOnlyList<string> Failures);

/// <summary>
/// The 이동 action (original <c>FileActions.move</c>): moves files and folders into a folder. An item already in that
/// folder is skipped, a folder never goes into itself or its own subfolder, and a name clash gets a number
/// (<c>a 2.txt</c>, <c>폴더 2</c>). Items are independent: one failure never stops the rest and nothing throws except cancellation.
/// </summary>
public static class FileMover
{
    const int ErrorAccessDenied = 5, ErrorNotSameDevice = 17, ErrorSharingViolation = 32, ErrorLockViolation = 33,
        ErrorHandleDiskFull = 39, ErrorFileExists = 80, ErrorDiskFull = 112, ErrorAlreadyExists = 183;
    const int MaxListedFailures = 3;

    /// <summary>File system calls a test can replace, so the cross-volume folder path runs without a second volume.</summary>
    internal sealed class Ops
    {
        public Action<string, string> MoveDirectory { get; init; } = Directory.Move;
        public Action<string, string> CopyFile { get; init; } = (s, d) => File.Copy(s, d);
    }

    /// <summary>Moves each item into <paramref name="destinationDir"/>. Cancellation is checked between items (and between files of a cross-volume folder copy).</summary>
    public static MoveResult Move(IReadOnlyList<string> paths, string destinationDir, OperationContext? context = null) =>
        Move(paths, destinationDir, new Ops(), context);

    internal static MoveResult Move(IReadOnlyList<string> paths, string destinationDir, Ops ops, OperationContext? context = null)
    {
        var token = context?.Token ?? CancellationToken.None;
        var destination = Normalize(destinationDir);
        var moved = new List<string>();
        var failures = new List<string>();
        for (var i = 0; i < paths.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var source = Normalize(paths[i]);
            var name = OutputNaming.DisplayName(source);
            var isFolder = Directory.Exists(source);
            if (!isFolder && !File.Exists(source))
                failures.Add($"‘{name}’을 찾을 수 없습니다.");
            else if (IsSameOrInside(destination, source))
                failures.Add($"‘{name}’은 자기 안으로 옮길 수 없습니다.");
            else if (Path.GetFileName(source).Length == 0)
                failures.Add($"‘{name}’은 옮길 수 없습니다."); // a drive or share root
            else if (!SamePath(Path.GetDirectoryName(source), destination))
            {
                var target = UniqueTarget(destination, Path.GetFileName(source), isFolder);
                try
                {
                    if (isFolder) MoveFolder(source, target, ops, token);
                    else File.Move(source, target);
                    moved.Add(target);
                }
                catch (SourceNotRemovedException)
                {
                    moved.Add(target);
                    failures.Add($"‘{name}’의 원래 폴더에 옮기지 못한 항목이 남아 있습니다.");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or LimeException)
                {
                    failures.Add($"‘{name}’을 옮기지 못했습니다: {Reason(e)}");
                }
            }
            context?.Report((i + 1.0) / paths.Count);
        }
        return new MoveResult(moved, failures);
    }

    /// <summary>The failed card's text: up to three failure lines, prefixed with a count when some items did move.</summary>
    public static string FailureSummary(MoveResult result)
    {
        var lines = result.Failures.Take(MaxListedFailures).ToList();
        if (result.Failures.Count > MaxListedFailures) lines.Add($"외 {result.Failures.Count - MaxListedFailures}개");
        var body = string.Join("\n", lines);
        return result.Moved.Count == 0 ? body : $"{result.Moved.Count}개를 옮겼고 {result.Failures.Count}개는 옮기지 못했습니다.\n{body}";
    }

    /// <summary>
    /// A rename when source and target share a volume. Across volumes <see cref="Directory.Move"/> cannot work, so the
    /// folder is copied and then exactly what was copied is removed from the source (see <see cref="RemoveCopied"/>).
    /// A failed or cancelled copy removes only what it created, and the source stays untouched.
    /// </summary>
    static void MoveFolder(string source, string target, Ops ops, CancellationToken token)
    {
        if (SamePath(Path.GetPathRoot(source), Path.GetPathRoot(target)))
        {
            try
            {
                ops.MoveDirectory(source, target);
                return;
            }
            catch (IOException e) when (Win32Code(e) == ErrorNotSameDevice)
            {
                // A volume mounted in a folder: same drive letter, different volume.
            }
        }

        var copied = new List<Copied>();
        try
        {
            CopyTree(source, target, ops, token, copied);
        }
        catch
        {
            RemoveCreated(copied);
            throw;
        }
        if (!RemoveCopied(copied)) throw new SourceNotRemovedException();
    }

    /// <summary>One file or folder the cross-volume copy created, with the source's state when it was copied.</summary>
    sealed record Copied(string Source, string Target, bool IsFolder, long Length, DateTime LastWriteUtc);

    /// <summary>
    /// Copies a folder tree, recording each folder and file it creates (in creation order, parents first). The folder's
    /// listing is taken once up front, so items that appear later are not copied (and stay in the source). A name that
    /// already exists in the target is never written into or recorded. Folder attributes and times are applied after
    /// each subtree, read-only last.
    /// </summary>
    static void CopyTree(string source, string target, Ops ops, CancellationToken token, List<Copied> copied)
    {
        var from = new DirectoryInfo(source);
        if (Exists(target)) throw new IOException(target, unchecked((int)(0x80070000 | ErrorAlreadyExists)));
        Directory.CreateDirectory(target);
        copied.Add(new Copied(source, target, IsFolder: true, 0, default));

        foreach (var entry in from.EnumerateFileSystemInfos().ToList())
        {
            token.ThrowIfCancellationRequested();
            var to = Path.Combine(target, entry.Name);
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new LimeException("링크가 들어 있는 폴더는 다른 드라이브로 옮길 수 없습니다.");
            if (entry is DirectoryInfo)
            {
                CopyTree(entry.FullName, to, ops, token, copied);
                continue;
            }
            if (Exists(to)) throw new IOException(to, unchecked((int)(0x80070000 | ErrorFileExists)));
            entry.Refresh();
            var file = (FileInfo)entry;
            var state = new Copied(entry.FullName, to, IsFolder: false, file.Length, file.LastWriteTimeUtc);
            copied.Add(state); // before the copy, so a half-written target is cleaned up too
            ops.CopyFile(entry.FullName, to);
        }

        var made = new DirectoryInfo(target)
        {
            CreationTimeUtc = from.CreationTimeUtc,
            LastWriteTimeUtc = from.LastWriteTimeUtc,
        };
        made.Attributes = FileAttributes.Directory | (from.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly));
    }

    /// <summary>
    /// Deletes from the source exactly the files that were copied and are unchanged since (same size and last-write
    /// time), then the copied folders deepest first, each only if empty. Anything added, changed or undeletable stays.
    /// Returns false when anything of the source is left.
    /// </summary>
    static bool RemoveCopied(List<Copied> copied)
    {
        var complete = true;
        for (var i = copied.Count - 1; i >= 0; i--)
        {
            var item = copied[i];
            try
            {
                if (item.IsFolder)
                {
                    var attributes = File.GetAttributes(item.Source);
                    ClearReadOnly(item.Source);
                    try
                    {
                        Directory.Delete(item.Source, recursive: false);
                    }
                    catch (IOException)
                    {
                        File.SetAttributes(item.Source, attributes); // still there (not empty): as it was
                        throw;
                    }
                }
                else
                {
                    var file = new FileInfo(item.Source);
                    if (!file.Exists || file.Length != item.Length || file.LastWriteTimeUtc != item.LastWriteUtc)
                    {
                        complete &= !file.Exists; // gone already is fine; changed stays
                        continue;
                    }
                    ClearReadOnly(item.Source);
                    file.Delete();
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                complete = false;
            }
        }
        return complete;
    }

    /// <summary>Undoes a failed copy: deletes only the files and folders it created, deepest first, folders only if empty.</summary>
    static void RemoveCreated(List<Copied> copied)
    {
        for (var i = copied.Count - 1; i >= 0; i--)
        {
            var target = copied[i].Target;
            try
            {
                if (copied[i].IsFolder)
                {
                    if (!Directory.Exists(target)) continue;
                    ClearReadOnly(target);
                    Directory.Delete(target, recursive: false);
                }
                else if (File.Exists(target))
                {
                    ClearReadOnly(target);
                    File.Delete(target);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Leaves part of the copy; the source is still complete, which is what matters.
            }
        }
    }

    static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Same name in the destination, numbered on a clash: files keep their extension last, folders are numbered as a whole.</summary>
    static string UniqueTarget(string destination, string name, bool isFolder)
    {
        var ext = isFolder ? "" : Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        return ext.Length > 1 && stem.Length > 0
            ? OutputNaming.UniquePath(destination, stem, ext[1..])
            : OutputNaming.UniqueName(destination, name);
    }

    /// <summary>A short Korean reason for a failed move.</summary>
    static string Reason(Exception e) => e switch
    {
        LimeException => e.Message,
        UnauthorizedAccessException => "접근 권한이 없습니다.",
        FileNotFoundException or DirectoryNotFoundException => "항목을 찾을 수 없습니다.",
        PathTooLongException => "경로가 너무 깁니다.",
        _ => Win32Code(e) switch
        {
            ErrorSharingViolation or ErrorLockViolation => "다른 프로그램이 사용 중입니다.",
            ErrorAccessDenied => "접근이 거부되었습니다. 다른 프로그램이 사용 중일 수 있습니다.",
            ErrorDiskFull or ErrorHandleDiskFull => "디스크 공간이 부족합니다.",
            ErrorFileExists or ErrorAlreadyExists => "같은 이름의 항목이 이미 있습니다.",
            _ => e.Message,
        },
    };

    /// <summary>The Win32 error code inside an HRESULT of the form 0x8007xxxx, else -1.</summary>
    static int Win32Code(Exception e) => (e.HResult & 0xFFFF0000) == 0x80070000 ? e.HResult & 0xFFFF : -1;

    static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static bool IsSameOrInside(string path, string folder) =>
        SamePath(path, folder) || path.StartsWith(Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>The copy is complete in the destination, but part of the source was left (added, changed or undeletable).</summary>
    sealed class SourceNotRemovedException : Exception;
}
