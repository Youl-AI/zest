using System.Runtime.InteropServices;
using System.Text;
using Zest.Core.Input;

namespace Zest.Core.Tests;

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
        Assert.False(DropSourcePolicy.IsRealUserFiles([Path.Combine(Temp, rel).ToUpperInvariant()], Temp));
    }

    [Theory]
    [InlineData(@"BNZ.1a2b3c\photo.png")]
    [InlineData(@"7zO12345678\photo.png")]
    [InlineData(@"Rar$DRa1234.567\photo.png")]
    [InlineData(@"Temp1_archive.zip\photo.png")]
    public void ArchiveExtractionFoldersAreRefusedInShortForm(string rel)
    {
        if (ShortForm(Temp) == Temp) Assert.Skip("8.3 names are disabled on this volume");
        Assert.False(DropSourcePolicy.IsRealUserFiles([Path.Combine(ShortForm(Temp), rel)], Temp));
        Assert.False(DropSourcePolicy.IsRealUserFiles([Path.Combine(Temp, rel)], ShortForm(Temp)));
    }

    [Fact]
    public void DevicePathPrefixIsIgnored()
    {
        Assert.False(DropSourcePolicy.IsRealUserFiles([@"\\?\" + Path.Combine(Temp, "BNZ.1", "a.png")], Temp));
        Assert.True(DropSourcePolicy.IsRealUserFiles([@"\\?\D:\Pictures\a.png"], Temp));
    }

    [Fact]
    public void OtherDriveAndUncPathsAreAcceptedWithoutAnyFileSystemProbe()
    {
        var probes = 0;
        bool Exists(string _) { probes++; return false; }
        string? Long(string _) { probes++; return null; }
        var other = Path.GetPathRoot(Temp)!.StartsWith('Z') ? @"Y:\" : @"Z:\";
        Assert.True(DropSourcePolicy.IsRealUserFiles(
            [other + @"Temp\x~1.png", @"\\nonexistent-host\share\x~1.txt", @"\\?\UNC\nonexistent-host\share\y~1.txt"], Temp, Exists, Long));
        Assert.Equal(0, probes);
    }

    [Fact]
    public void UncDevicePrefixIsNormalizedAndStillRefusedInsideTemp()
    {
        Assert.True(DropSourcePolicy.IsRealUserFiles([@"\\?\UNC\host\share\a.png"], Temp, _ => false, _ => null));
        Assert.False(DropSourcePolicy.IsRealUserFiles([@"\\?\" + Path.Combine(Temp, "BNZ.1", "a.png")], Temp));
    }

    [Fact]
    public void UncDevicePathInsideAUncTempRoot_IsRefused_WithoutAnyFileSystemProbe()
    {
        var probes = 0;
        bool Exists(string _) { probes++; return false; }
        string? Long(string _) { probes++; return null; }
        Assert.False(DropSourcePolicy.IsRealUserFiles([@"\\?\UNC\host\share\tmp\BNZ.1\a.png"], @"\\host\share\tmp", Exists, Long));
        Assert.False(DropSourcePolicy.IsRealUserFiles([@"\\host\share\tmp\BNZ.1\a.png"], @"\\host\share\tmp", Exists, Long));
        Assert.True(DropSourcePolicy.IsRealUserFiles([@"\\?\UNC\host\share\tmp2\a.png"], @"\\host\share\tmp", Exists, Long));
        Assert.Equal(0, probes);
    }

    [Fact]
    public void TempRootIsExpandedOncePerCall()
    {
        var longCalls = 0;
        var tilde = Path.Combine(Path.GetPathRoot(Temp)!, "Users", "HAYOUL~1", "Temp");
        string? Long(string p) { longCalls++; return null; }
        var outside = Path.Combine(Path.GetPathRoot(Temp)!, "Other");
        Assert.True(DropSourcePolicy.IsRealUserFiles([outside + @"\a~1.png", outside + @"\b~1.png", outside + @"\c~1.png"], tilde, _ => true, Long));
        Assert.Equal(4, longCalls); // one per path, plus the temp root once
    }

    [Fact]
    public void ExistingFileInShortFormTempIsRefused()
    {
        if (ShortForm(Temp) == Temp) Assert.Skip("8.3 names are disabled on this volume");
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
    public void FileInsideTempIsRefusedWhenTheRootHasATrailingSlash()
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
