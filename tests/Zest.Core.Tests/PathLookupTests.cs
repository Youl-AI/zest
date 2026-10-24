namespace Zest.Core.Tests;

public class PathLookupTests
{
    [Fact]
    public void Find_FirstFolderThatHasIt_SkippingQuotesBlanksAndBadEntries()
    {
        var empty = TestFiles.TempDir();
        var first = TestFiles.TempDir();
        var second = TestFiles.TempDir();
        TestFiles.Touch(first, "tool.exe");
        TestFiles.Touch(second, "tool.exe");
        var list = string.Join(Path.PathSeparator, ["", "bad|dir", empty, $"\"{first}\"", second]);

        Assert.Equal(Path.Combine(first, "tool.exe"), PathLookup.Find("tool.exe", list));
    }

    [Fact]
    public void Find_Nowhere_IsNull()
    {
        Assert.Null(PathLookup.Find("tool.exe", TestFiles.TempDir()));
        Assert.Null(PathLookup.Find("tool.exe", null));
    }
}
