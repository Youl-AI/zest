using Zest.Core.Output;
using Zest.Core.Wheel;

namespace Zest.Core.Tests;

public class HubSummaryTests
{
    static HubEntry File(long? bytes) => new(IsFolder: false, bytes);
    static readonly HubEntry Folder = new(IsFolder: true, null);

    [Fact]
    public void OneFile_ShowsItsSize() =>
        Assert.Equal("5.6 MB", HubSummary.For([File(5_600_000)]));

    [Fact]
    public void SeveralFiles_ShowCountAndTotal() =>
        Assert.Equal("3개 · 12.4 MB", HubSummary.For([File(5_600_000), File(4_300_000), File(2_500_000)]));

    [Fact]
    public void FilesAndAFolder_ShowBothCountsWithoutSizes() =>
        Assert.Equal("파일 2개 · 폴더 1개", HubSummary.For([File(5_600_000), Folder, File(1_000)]));

    [Fact]
    public void SingleFolder_ShowsFolderCount() =>
        Assert.Equal("폴더 1개", HubSummary.For([Folder]));

    [Fact]
    public void FoldersOnly_ShowFolderCount() =>
        Assert.Equal("폴더 2개", HubSummary.For([Folder, Folder]));

    [Fact]
    public void UnreadableSize_IsLeftOutOfTheTotal() =>
        Assert.Equal("3개 · 7.6 MB", HubSummary.For([File(5_600_000), File(null), File(2_000_000)]));

    [Fact]
    public void NoReadableSize_ShowsCountOnly() =>
        Assert.Equal("3개", HubSummary.For([File(null), File(null), File(null)]));

    [Fact]
    public void SingleUnreadableFile_ShowsNothing() =>
        Assert.Equal("", HubSummary.For([File(null)]));

    [Fact]
    public void EmptyFile_ShowsZeroBytes() =>
        Assert.Equal("0 B", HubSummary.For([File(0)]));

    [Fact]
    public void NoEntries_ShowsNothing() =>
        Assert.Equal("", HubSummary.For([]));

    [Fact]
    public void Read_FileGivesItsLength()
    {
        var path = Path.Combine(Path.GetTempPath(), "zest-hub-" + Guid.NewGuid().ToString("N") + ".bin");
        System.IO.File.WriteAllBytes(path, new byte[1234]);
        try
        {
            Assert.Equal(new HubEntry(false, 1234), HubEntry.Read(path));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Read_FolderIsAFolder() =>
        Assert.Equal(new HubEntry(true, null), HubEntry.Read(Path.GetTempPath()));

    [Fact]
    public void Read_MissingPathIsAFileOfUnknownSize() =>
        Assert.Equal(new HubEntry(false, null), HubEntry.Read(Path.Combine(Path.GetTempPath(), "zest-missing-" + Guid.NewGuid().ToString("N"))));
}

public class SizeTextTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(182_400, "182 KB")]
    [InlineData(1_400_000, "1.4 MB")]
    [InlineData(5_000_000, "5 MB")]
    public void Format_UsesDecimalUnits(long bytes, string text) =>
        Assert.Equal(text, SizeText.Format(bytes));
}
