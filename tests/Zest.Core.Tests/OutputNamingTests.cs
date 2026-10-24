using Zest.Core.Jobs;
using Zest.Core.Output;
using static Zest.Core.Tests.TestFiles;

namespace Zest.Core.Tests;

public class OutputNamingTests
{
    static OperationContext Ctx() => new(CancellationToken.None);

    [Fact] public void ReservePath_AppendsNumberFrom2() { var d = TempDir(); File.WriteAllText(Path.Combine(d, "a.jpg"), "x"); Assert.Equal(Path.Combine(d, "a 2.jpg"), OutputNaming.ReservePath(d, "a", "jpg")); Assert.Equal("x", File.ReadAllText(Path.Combine(d, "a.jpg"))); }
    [Fact] public void ReservePath_CreatesAnEmptyPlaceholder() { var d = TempDir(); var p = OutputNaming.ReservePath(d, "a", "jpg"); Assert.Equal(Path.Combine(d, "a.jpg"), p); Assert.True(File.Exists(p)); Assert.Equal(0, new FileInfo(p).Length); }
    [Fact] public void ReservePath_AReservedNameIsTakenBeforeItIsWritten() { var d = TempDir(); Assert.Equal(Path.Combine(d, "a.jpg"), OutputNaming.ReservePath(d, "a", "jpg")); Assert.Equal(Path.Combine(d, "a 2.jpg"), OutputNaming.ReservePath(d, "a", "jpg")); }
    [Fact] public void ReservePath_SkipsAFolderOfThatName() { var d = TempDir(); Directory.CreateDirectory(Path.Combine(d, "a.jpg")); Assert.Equal(Path.Combine(d, "a 2.jpg"), OutputNaming.ReservePath(d, "a", "jpg")); }

    [Fact]
    public void ReservePath_ConcurrentCallers_EachGetTheirOwnName()
    {
        var d = TempDir();
        var names = new string[16];
        Parallel.For(0, names.Length, i => names[i] = OutputNaming.ReservePath(d, "a", "webp"));
        Assert.Equal(16, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(16, Directory.GetFiles(d).Length);
    }

    [Fact] public void ToolOutput_TagThenNumber_AndTracksIt() { var d = TempDir(); var src = Touch(d, "사진.jpg"); Touch(d, "사진 (compressed).jpg"); var ctx = Ctx(); var p = OutputNaming.ToolOutput(src, "compressed", "jpg", ctx); Assert.Equal(Path.Combine(d, "사진 (compressed) 2.jpg"), p); Assert.True(File.Exists(p)); Assert.Equal([p], ctx.Created); }
    [Fact] public void ConvertOutput_ClashGetsNumber_AndTracksIt() { var d = TempDir(); var src = Touch(d, "a.png"); var ctx = Ctx(); Assert.Equal(Path.Combine(d, "a.jpg"), OutputNaming.ConvertOutput(src, "jpg", ctx)); Assert.Equal(Path.Combine(d, "a 2.jpg"), OutputNaming.ConvertOutput(src, "jpg", ctx)); Assert.Equal([Path.Combine(d, "a.jpg"), Path.Combine(d, "a 2.jpg")], ctx.Created); }

    [Theory] [InlineData(3, 9, "a 3.png")] [InlineData(3, 12, "a 03.png")] [InlineData(7, 100, "a 007.png")]
    public void PageFileName_PadsToCountDigits(int i, int n, string expected) => Assert.Equal(expected, OutputNaming.PageFileName("a", i, n, "png"));

    [Fact] public void CreatePagesFolder_IsUnique_AndTracked() { var d = TempDir(); var src = Touch(d, "doc.pdf"); Directory.CreateDirectory(Path.Combine(d, "doc (pages)")); var ctx = Ctx(); Assert.Equal(Path.Combine(d, "doc (pages) 2"), OutputNaming.CreatePagesFolder(src, ctx)); Assert.True(Directory.Exists(Path.Combine(d, "doc (pages) 2"))); Assert.Equal([Path.Combine(d, "doc (pages) 2")], ctx.Created); }
    [Fact] public void ReserveFolder_SkipsAFileOfThatName() { var d = TempDir(); Touch(d, "doc (pages)"); Assert.Equal(Path.Combine(d, "doc (pages) 2"), OutputNaming.ReserveFolder(d, "doc (pages)")); }

    [Fact]
    public void ReserveFolder_ConcurrentCallers_EachGetTheirOwnFolder()
    {
        var d = TempDir();
        var names = new string[16];
        Parallel.For(0, names.Length, i => names[i] = OutputNaming.ReserveFolder(d, "doc (pages)"));
        Assert.Equal(16, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(16, Directory.GetDirectories(d).Length);
    }

    [Fact] public void TryCreateFolder_NewName_CreatesIt() { var d = TempDir(); var p = Path.Combine(d, "a_files"); Assert.True(OutputNaming.TryCreateFolder(p)); Assert.True(Directory.Exists(p)); }
    [Fact] public void TryCreateFolder_ExistingFolder_IsFalse() { var d = TempDir(); var p = Directory.CreateDirectory(Path.Combine(d, "a_files")).FullName; File.WriteAllText(Path.Combine(p, "keep.png"), "x"); Assert.False(OutputNaming.TryCreateFolder(p)); Assert.Equal("x", File.ReadAllText(Path.Combine(p, "keep.png"))); }
    [Fact] public void TryCreateFolder_ExistingFile_IsFalse() { var d = TempDir(); var p = Touch(d, "a_files"); Assert.False(OutputNaming.TryCreateFolder(p)); Assert.True(File.Exists(p)); }
    [Fact] public void TryCreateFolder_LongPath_Works() { var d = TempDir(); var p = Path.Combine(d, new string('가', 120), new string('나', 120)); Directory.CreateDirectory(Path.GetDirectoryName(p)!); Assert.True(OutputNaming.TryCreateFolder(p)); Assert.True(Directory.Exists(p)); Directory.Delete(Path.Combine(d, new string('가', 120)), true); }

    // FreePath / FreeName only pick a name (FileMover, whose own move refuses an existing target).
    [Fact] public void FreePath_AppendsNumberFrom2_AndCreatesNothing() { var d = TempDir(); Touch(d, "a.jpg"); Assert.Equal(Path.Combine(d, "a 2.jpg"), OutputNaming.FreePath(d, "a", "jpg")); Assert.Single(Directory.GetFileSystemEntries(d)); }
    [Fact] public void FreeName_Free_KeepsName() { var d = TempDir(); Assert.Equal(Path.Combine(d, "사진.2024"), OutputNaming.FreeName(d, "사진.2024")); Assert.Empty(Directory.GetFileSystemEntries(d)); }
    [Fact] public void FreeName_NumbersWholeName() { var d = TempDir(); Directory.CreateDirectory(Path.Combine(d, "사진.2024")); Touch(d, "사진.2024 2"); Assert.Equal(Path.Combine(d, "사진.2024 3"), OutputNaming.FreeName(d, "사진.2024")); }

    [Theory] [InlineData(@"C:\a\b.txt", "b.txt")] [InlineData(@"C:\a\폴더\", "폴더")] [InlineData(@"C:\", @"C:\")] [InlineData(@"\\server\share\", @"\\server\share\")]
    public void DisplayName_UsesLastPartOrWholeRoot(string path, string expected) => Assert.Equal(expected, OutputNaming.DisplayName(path));
}
