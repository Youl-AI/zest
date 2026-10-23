using System.Runtime.InteropServices;
using System.Text;
using LimeWin.Core.Office;

namespace LimeWin.Core.Tests;

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
