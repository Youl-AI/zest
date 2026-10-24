using System.Runtime.InteropServices;
using System.Text;
using Zest.Core.Office;

namespace Zest.Core.Tests;

/// <summary>The private Office work folder: which open documents are ours (no Office needed).</summary>
public class OfficeWorkFolderTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);

    static string ShortForm(string p)
    {
        var sb = new StringBuilder(1024);
        return GetShortPathName(p, sb, sb.Capacity) > 0 ? sb.ToString() : p;
    }

    static string Folder(string root, string name, TimeSpan age, DateTime now)
    {
        var path = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        Directory.CreateDirectory(Path.Combine(path, "in"));
        File.WriteAllText(Path.Combine(path, "in", "x.docx"), "x");
        Directory.SetCreationTimeUtc(path, now - age);
        Directory.SetLastWriteTimeUtc(path, now - age);
        return path;
    }

    [Fact]
    public void SweepStale_DeletesOnlyOldWorkFolders()
    {
        var now = DateTime.UtcNow;
        var old = Folder(Dir, Guid.NewGuid().ToString("N"), TimeSpan.FromHours(2), now);
        var recent = Folder(Dir, Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(10), now);
        var notOurs = Folder(Dir, "something else", TimeSpan.FromDays(3), now);
        var file = TestFiles.Touch(Dir, Guid.NewGuid().ToString("N"));
        File.SetLastWriteTimeUtc(file, now - TimeSpan.FromDays(3));

        OfficeWorkFolder.SweepStale(Dir, now, TimeSpan.FromHours(1));

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
        Assert.True(Directory.Exists(notOurs));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void SweepStale_LockedOrMissingFolders_DoNotThrow()
    {
        var now = DateTime.UtcNow;
        var locked = Folder(Dir, Guid.NewGuid().ToString("N"), TimeSpan.FromHours(2), now);
        using (new FileStream(Path.Combine(locked, "in", "x.docx"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            OfficeWorkFolder.SweepStale(Dir, now, TimeSpan.FromHours(1));
        }
        OfficeWorkFolder.SweepStale(Path.Combine(Dir, "missing"), now, TimeSpan.FromHours(1));
    }

    [Fact]
    public void CopyCompanionFiles_CopiesTheSiblingFilesFolderNextToTheInput()
    {
        var src = TestFiles.Touch(Dir, "그림.html");
        var files = Directory.CreateDirectory(Path.Combine(Dir, "그림_files", "sub")).FullName;
        File.WriteAllText(Path.Combine(Dir, "그림_files", "image001.png"), "png");
        File.WriteAllText(Path.Combine(files, "x.xml"), "xml");

        using var work = OfficeWorkFolder.Create();
        work.CopyCompanionFiles(src);

        Assert.Equal("png", File.ReadAllText(Path.Combine(work.In, "그림_files", "image001.png")));
        Assert.Equal("xml", File.ReadAllText(Path.Combine(work.In, "그림_files", "sub", "x.xml")));
        Assert.Equal("png", File.ReadAllText(Path.Combine(Dir, "그림_files", "image001.png"))); // the user's folder is untouched
    }

    [Fact]
    public void CopyCompanionFiles_NoFolder_CopiesNothing()
    {
        var src = TestFiles.Touch(Dir, "그림.html");
        using var work = OfficeWorkFolder.Create();
        work.CopyCompanionFiles(src);
        Assert.Empty(Directory.GetFileSystemEntries(work.In));
    }

    [Fact]
    public void IsOurCopy_MatchesOurInputOrSavedPath_IgnoringCase()
    {
        var input = TestFiles.Touch(Dir, "0f1e.txt");
        var saved = Path.Combine(Dir, "가계부.csv");
        Assert.True(OfficeWorkFolder.IsOurCopy(input.ToUpperInvariant(), input, saved));
        Assert.True(OfficeWorkFolder.IsOurCopy(saved, input, saved));
    }

    [Fact]
    public void IsOurCopy_SameNameInAnotherFolder_IsNotOurs()
    {
        // The user's own workbook can have the very name our copy was saved under (e.g. "가계부.csv").
        var input = TestFiles.Touch(Dir, "0f1e.txt");
        var saved = Path.Combine(Dir, "가계부.csv");
        var users = Path.Combine(TestFiles.TempDir(), "가계부.csv");
        Assert.False(OfficeWorkFolder.IsOurCopy(users, input, saved));
        Assert.False(OfficeWorkFolder.IsOurCopy("가계부.csv", input, saved)); // an unsaved book's FullName is just its name
        Assert.False(OfficeWorkFolder.IsOurCopy(null, input, saved));
        Assert.False(OfficeWorkFolder.IsOurCopy("", input, saved));
    }

    [Fact]
    public void IsOurCopy_ShortAndLongFormsOfTheSamePathMatch()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Dir, "long folder name")).FullName;
        var input = TestFiles.Touch(folder, "copy of input.txt");
        var shortInput = ShortForm(input);
        if (shortInput == input) Assert.Skip("8.3 names are disabled on this volume");
        Assert.True(OfficeWorkFolder.IsOurCopy(shortInput, input, null));
        Assert.True(OfficeWorkFolder.IsOurCopy(input, shortInput, null));
    }
}
