using System.Runtime.InteropServices;
using System.Text;
using LimeWin.Core.Input;

namespace LimeWin.Core.Tests;

public class DropSourcePolicyTests
{
    const char Sep = (char)92;
    static readonly string Temp = Path.GetTempPath().TrimEnd(Sep);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);

    static string ShortForm(string p)
    {
        var sb = new StringBuilder(1024);
        return GetShortPathName(p, sb, sb.Capacity) > 0 ? sb.ToString() : p;
    }

    [Theory]
    [InlineData(@"BNZ.1a2b3c\photo.png")]
    [InlineData(@"7zO12345678\photo.png")]
    [InlineData(@"Rar$DRa1234.567\photo.png")]
    [InlineData(@"Temp1_archive.zip\photo.png")]
    public void ArchiveExtractionFoldersAreRefused(string rel)
    {
        Assert.False(DropSourcePolicy.IsRealUserFiles([Path.Combine(Temp, rel)], Temp));
        Assert.False(DropSourcePolicy.IsRealUserFiles([Path.Combine(ShortForm(Temp), rel)], Temp));
        Assert.False(DropSourcePolicy.IsRealUserFiles([Path.Combine(Temp, rel)], ShortForm(Temp)));
        Assert.False(DropSourcePolicy.IsRealUserFiles([Path.Combine(Temp, rel).ToUpperInvariant()], Temp));
    }

    [Fact]
    public void ExistingFileInShortFormTempIsRefused()
    {
        var dir = TestFiles.TempDir();
        var file = TestFiles.Touch(dir, "a.png");
        var shortFile = ShortForm(file);
        Assert.False(DropSourcePolicy.IsRealUserFiles([shortFile], Temp));
    }

    [Fact]
    public void NormalUserFileIsAccepted()
    {
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "photo.png");
        Assert.True(DropSourcePolicy.IsRealUserFiles([desktop], Temp));
        Assert.True(DropSourcePolicy.IsRealUserFiles([@"D:\Pictures\a.png", @"C:\Users\x\b.png"], @"C:\Users\x\AppData\Local\Temp"));
    }

    [Fact]
    public void AnyTempPathInAMixedListRefusesTheWholeDrop()
    {
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "photo.png");
        Assert.False(DropSourcePolicy.IsRealUserFiles([desktop, Path.Combine(Temp, "BNZ.1", "b.png")], Temp));
    }

    [Fact]
    public void SiblingFolderWithTheSamePrefixIsNotTemp()
    {
        Assert.True(DropSourcePolicy.IsRealUserFiles([Temp + @"2\x.png"], Temp));
        Assert.True(DropSourcePolicy.IsRealUserFiles([Temp + @"-backup\x.png"], Temp));
    }

    [Fact]
    public void TempFolderItselfIsRefusedAndTrailingSlashIsTolerated()
    {
        Assert.False(DropSourcePolicy.IsRealUserFiles([Path.Combine(Temp, "x.png")], Temp + "\\"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(@"photo.png")]
    [InlineData(@"..\photo.png")]
    public void EmptyOrRelativePathsAreRefused(string path)
    {
        Assert.False(DropSourcePolicy.IsRealUserFiles([path], Temp));
        Assert.False(DropSourcePolicy.IsRealUserFiles([@"D:\ok.png", path], Temp));
    }

    [Fact]
    public void EmptyListIsRefused() => Assert.False(DropSourcePolicy.IsRealUserFiles([], Temp));
}
