using LimeWin.Core.Actions;

namespace LimeWin.Core.Tests;

public class FileMoverTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    string Folder(string relative) => Directory.CreateDirectory(Path.Combine(Dir, relative)).FullName;

    static string Write(string dir, string name, string text)
    {
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void Move_File_GoesToDestination()
    {
        var src = Folder("src");
        var dest = Folder("dest");
        var file = Write(src, "a.txt", "hello");

        var r = FileMover.Move([file], dest);

        Assert.Equal([Path.Combine(dest, "a.txt")], r.Moved);
        Assert.Empty(r.Failures);
        Assert.False(File.Exists(file));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Move_NameClash_GetsNumber()
    {
        var src = Folder("src");
        var dest = Folder("dest");
        var file = Write(src, "a.txt", "new");
        Write(dest, "a.txt", "old");

        var r = FileMover.Move([file], dest);

        Assert.Equal([Path.Combine(dest, "a 2.txt")], r.Moved);
        Assert.False(File.Exists(file));
        Assert.Equal("old", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "a 2.txt")));
    }

    [Fact]
    public void Move_FolderNameClash_NumbersWholeName()
    {
        var src = Folder("src");
        var dest = Folder("dest");
        var folder = Folder(Path.Combine("src", "사진.2024"));
        Write(folder, "x.txt", "x");
        Folder(Path.Combine("dest", "사진.2024"));

        var r = FileMover.Move([folder], dest);

        Assert.Equal([Path.Combine(dest, "사진.2024 2")], r.Moved);
        Assert.True(File.Exists(Path.Combine(dest, "사진.2024 2", "x.txt")));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Move_ExtensionlessFileClash_GetsNumber()
    {
        var src = Folder("src");
        var dest = Folder("dest");
        var file = Write(src, "README", "new");
        Write(dest, "README", "old");

        var r = FileMover.Move([file], dest);

        Assert.Equal([Path.Combine(dest, "README 2")], r.Moved);
    }

    [Fact]
    public void Move_IntoItself_Fails()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Dir, "폴더")).FullName;

        var r = FileMover.Move([dir], Path.Combine(dir, "안"));

        Assert.Equal(["‘폴더’은 자기 안으로 옮길 수 없습니다."], r.Failures);
        Assert.Empty(r.Moved);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void Move_OntoItself_Fails()
    {
        var dir = Folder("폴더");

        var r = FileMover.Move([dir], dir + Path.DirectorySeparatorChar);

        Assert.Single(r.Failures);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void Move_SiblingWithSamePrefix_IsNotInsideItself()
    {
        var dir = Folder("ab");
        var sibling = Folder("abc");

        var r = FileMover.Move([dir], sibling);

        Assert.Empty(r.Failures);
        Assert.True(Directory.Exists(Path.Combine(sibling, "ab")));
    }

    [Fact]
    public void Move_AlreadyThere_Skipped()
    {
        var dest = Folder("dest");
        var file = Write(dest, "a.txt", "x");

        var r = FileMover.Move([file], dest.ToUpperInvariant());

        Assert.Empty(r.Moved);
        Assert.Empty(r.Failures);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(Path.Combine(dest, "a 2.txt")));
    }

    [Fact]
    public void Move_Folder_MovesContents()
    {
        var dest = Folder("dest");
        var folder = Folder(Path.Combine("src", "프로젝트"));
        Write(folder, "a.txt", "a");
        Write(Folder(Path.Combine("src", "프로젝트", "sub")), "b.txt", "b");

        var r = FileMover.Move([folder], dest);

        var moved = Path.Combine(dest, "프로젝트");
        Assert.Equal([moved], r.Moved);
        Assert.Equal("b", File.ReadAllText(Path.Combine(moved, "sub", "b.txt")));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Move_Missing_Fails()
    {
        var dest = Folder("dest");

        var r = FileMover.Move([Path.Combine(Dir, "없음.txt")], dest);

        Assert.Equal(["‘없음.txt’을 찾을 수 없습니다."], r.Failures);
    }

    [Fact]
    public void Move_LockedFile_FailsWithReason_OthersStillMove()
    {
        var src = Folder("src");
        var dest = Folder("dest");
        var locked = Write(src, "locked.txt", "x");
        var free = Write(src, "free.txt", "y");

        MoveResult r;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            r = FileMover.Move([locked, free], dest);

        Assert.Equal(["‘locked.txt’을 옮기지 못했습니다: 다른 프로그램이 사용 중입니다."], r.Failures);
        Assert.Equal([Path.Combine(dest, "free.txt")], r.Moved);
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void Move_FolderAcrossVolumes_CopiesThenDeletesSource()
    {
        var dest = Folder("dest");
        var folder = Folder(Path.Combine("src", "프로젝트"));
        Write(folder, "a.txt", "a");
        Write(Folder(Path.Combine("src", "프로젝트", "sub")), "b.txt", "b");
        Folder(Path.Combine("src", "프로젝트", "empty"));

        var r = FileMover.Move([folder], dest, ops: new FileMover.Ops { MoveDirectory = CrossVolume });

        var moved = Path.Combine(dest, "프로젝트");
        Assert.Equal([moved], r.Moved);
        Assert.Empty(r.Failures);
        Assert.Equal("a", File.ReadAllText(Path.Combine(moved, "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(moved, "sub", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(moved, "empty")));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Move_FolderAcrossVolumes_CopyFails_KeepsSourceAndRemovesPartialCopy()
    {
        var dest = Folder("dest");
        var folder = Folder(Path.Combine("src", "프로젝트"));
        Write(folder, "a.txt", "a");
        Write(folder, "b.txt", "b");
        var copies = 0;
        var ops = new FileMover.Ops
        {
            MoveDirectory = CrossVolume,
            CopyFile = (s, d) =>
            {
                if (++copies == 2) throw new IOException("disk full", unchecked((int)0x80070070)); // ERROR_DISK_FULL
                File.Copy(s, d);
            },
        };

        var r = FileMover.Move([folder], dest, ops: ops);

        Assert.Empty(r.Moved);
        Assert.Equal(["‘프로젝트’을 옮기지 못했습니다: 디스크 공간이 부족합니다."], r.Failures);
        Assert.True(File.Exists(Path.Combine(folder, "a.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "b.txt")));
        Assert.False(Directory.Exists(Path.Combine(dest, "프로젝트")));
    }

    [Fact]
    public void Move_FolderAcrossVolumes_FileAddedDuringCopy_StaysInSource()
    {
        var dest = Folder("dest");
        var folder = Folder(Path.Combine("src", "프로젝트"));
        Write(folder, "a.txt", "a");
        Write(Folder(Path.Combine("src", "프로젝트", "sub")), "b.txt", "b");
        var ops = new FileMover.Ops
        {
            MoveDirectory = CrossVolume,
            CopyFile = (s, d) =>
            {
                File.Copy(s, d);
                if (Path.GetFileName(s) == "a.txt") Write(folder, "new.txt", "saved meanwhile");
            },
        };

        var r = FileMover.Move([folder], dest, ops: ops);

        var moved = Path.Combine(dest, "프로젝트");
        Assert.Equal([moved], r.Moved);
        Assert.Equal(["‘프로젝트’의 원래 폴더에 옮기지 못한 항목이 남아 있습니다."], r.Failures);
        Assert.Equal("saved meanwhile", File.ReadAllText(Path.Combine(folder, "new.txt")));
        Assert.False(File.Exists(Path.Combine(folder, "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(folder, "sub")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(moved, "sub", "b.txt")));
    }

    [Fact]
    public void Move_FolderAcrossVolumes_FileChangedAfterCopy_StaysInSource()
    {
        var dest = Folder("dest");
        var folder = Folder(Path.Combine("src", "프로젝트"));
        var a = Write(folder, "a.txt", "a");
        Write(folder, "b.txt", "b");
        var ops = new FileMover.Ops
        {
            MoveDirectory = CrossVolume,
            CopyFile = (s, d) =>
            {
                File.Copy(s, d);
                if (Path.GetFileName(s) == "b.txt") File.AppendAllText(a, " edited"); // a.txt was copied before this edit
            },
        };

        var r = FileMover.Move([folder], dest, ops: ops);

        Assert.Equal(["‘프로젝트’의 원래 폴더에 옮기지 못한 항목이 남아 있습니다."], r.Failures);
        Assert.Equal("a edited", File.ReadAllText(a));
        Assert.False(File.Exists(Path.Combine(folder, "b.txt")));
    }

    [Fact]
    public void Move_FolderAcrossVolumes_TargetAppearsMeanwhile_IsLeftAlone()
    {
        var dest = Folder("dest");
        var folder = Folder(Path.Combine("src", "프로젝트"));
        Write(folder, "a.txt", "a");
        var theirs = Path.Combine(dest, "프로젝트");
        var ops = new FileMover.Ops
        {
            MoveDirectory = (s, d) =>
            {
                // Someone else creates the target name after the clash check.
                Write(Directory.CreateDirectory(theirs).FullName, "theirs.txt", "theirs");
                CrossVolume(s, d);
            },
        };

        var r = FileMover.Move([folder], dest, ops: ops);

        Assert.Empty(r.Moved);
        Assert.Single(r.Failures);
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(theirs, "theirs.txt")));
        Assert.False(File.Exists(Path.Combine(theirs, "a.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "a.txt")));
    }

    [Fact]
    public void Move_FolderAcrossVolumes_KeepsFolderAttributesAndTimes()
    {
        var dest = Folder("dest");
        var folder = Folder(Path.Combine("src", "프로젝트"));
        var sub = Folder(Path.Combine("src", "프로젝트", "sub"));
        Write(sub, "b.txt", "b");
        var stamp = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(sub, stamp);
        Directory.SetCreationTimeUtc(sub, stamp);
        File.SetAttributes(sub, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.ReadOnly);

        var r = FileMover.Move([folder], dest, ops: new FileMover.Ops { MoveDirectory = CrossVolume });

        Assert.Empty(r.Failures);
        var copied = new DirectoryInfo(Path.Combine(dest, "프로젝트", "sub"));
        Assert.Equal(stamp, copied.LastWriteTimeUtc);
        Assert.Equal(stamp, copied.CreationTimeUtc);
        Assert.True(copied.Attributes.HasFlag(FileAttributes.Hidden));
        Assert.True(copied.Attributes.HasFlag(FileAttributes.ReadOnly));
        Assert.False(Directory.Exists(folder));
        copied.Attributes = FileAttributes.Directory; // let Dispose clean up
    }

    [Fact]
    public void Move_FolderAcrossVolumes_LinkInside_RefusedAndSourceKept()
    {
        var dest = Folder("dest");
        var outside = Folder("outside");
        Write(outside, "keep.txt", "keep");
        var folder = Folder(Path.Combine("src", "프로젝트"));
        Write(folder, "a.txt", "a");
        Junction(Path.Combine(folder, "link"), outside);

        var r = FileMover.Move([folder], dest, ops: new FileMover.Ops { MoveDirectory = CrossVolume });

        Assert.Empty(r.Moved);
        Assert.Equal(["‘프로젝트’을 옮기지 못했습니다: 링크가 들어 있는 폴더는 다른 드라이브로 옮길 수 없습니다."], r.Failures);
        Assert.True(File.Exists(Path.Combine(folder, "a.txt")));
        Assert.True(Directory.Exists(Path.Combine(folder, "link")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(dest, "프로젝트")));
    }

    [Fact]
    public void Move_FolderAcrossVolumes_CancelledMidCopy_RemovesPartialCopyKeepsSource()
    {
        var dest = Folder("dest");
        var folder = Folder(Path.Combine("src", "프로젝트"));
        Write(folder, "a.txt", "a");
        Write(folder, "b.txt", "b");
        using var cts = new CancellationTokenSource();
        var ops = new FileMover.Ops
        {
            MoveDirectory = CrossVolume,
            CopyFile = (s, d) =>
            {
                File.Copy(s, d);
                cts.Cancel();
            },
        };

        Assert.Throws<OperationCanceledException>(() => FileMover.Move([folder], dest, ops, new Jobs.OperationContext(cts.Token)));
        Assert.True(File.Exists(Path.Combine(folder, "a.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "b.txt")));
        Assert.False(Directory.Exists(Path.Combine(dest, "프로젝트")));
    }

    [Fact]
    public void Move_CancelledAfterFirstItem_StopsBeforeNext()
    {
        var src = Folder("src");
        var dest = Folder("dest");
        var a = Write(src, "a.txt", "a");
        var b = Write(src, "b.txt", "b");
        using var cts = new CancellationTokenSource();
        var context = new Jobs.OperationContext(cts.Token, new SyncProgress(_ => cts.Cancel()));

        Assert.Throws<OperationCanceledException>(() => FileMover.Move([a, b], dest, context));
        Assert.True(File.Exists(Path.Combine(dest, "a.txt")));
        Assert.True(File.Exists(b));
        Assert.False(File.Exists(Path.Combine(dest, "b.txt")));
    }

    [Fact]
    public void Summary_SomeMoved_SaysPartway()
    {
        var r = new MoveResult(["x", "y"], ["‘a’을 찾을 수 없습니다."]);

        Assert.Equal("2개를 옮겼고 1개는 옮기지 못했습니다.\n‘a’을 찾을 수 없습니다.", FileMover.FailureSummary(r));
    }

    [Fact]
    public void Summary_ManyFailures_ListsThreeAndCountsTheRest()
    {
        var r = new MoveResult([], ["1", "2", "3", "4", "5"]);

        Assert.Equal("1\n2\n3\n외 2개", FileMover.FailureSummary(r));
    }

    /// <summary>What <see cref="Directory.Move"/> throws when the destination is on another volume (ERROR_NOT_SAME_DEVICE).</summary>
    static void CrossVolume(string source, string destination) =>
        throw new IOException("Cannot move to a different disk drive.", unchecked((int)0x80070011));

    /// <summary>A directory junction (no privilege needed, unlike a symbolic link).</summary>
    static void Junction(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var p = System.Diagnostics.Process.Start(start)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(Directory.Exists(link), "junction was not created");
    }

    /// <summary>Reports synchronously on the calling thread (Progress&lt;T&gt; would post).</summary>
    sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
