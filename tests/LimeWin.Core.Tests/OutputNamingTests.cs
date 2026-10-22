using LimeWin.Core.Output;
using static LimeWin.Core.Tests.TestFiles;

namespace LimeWin.Core.Tests;

public class OutputNamingTests
{
    [Fact] public void UniquePath_AppendsNumberFrom2() { var d = TempDir(); File.WriteAllText(Path.Combine(d, "a.jpg"), ""); Assert.Equal(Path.Combine(d, "a 2.jpg"), OutputNaming.UniquePath(d, "a", "jpg")); }
    [Fact] public void ToolOutput_TagThenNumber() { var d = TempDir(); var src = Touch(d, "사진.jpg"); Touch(d, "사진 (compressed).jpg"); Assert.Equal(Path.Combine(d, "사진 (compressed) 2.jpg"), OutputNaming.ToolOutput(src, "compressed", "jpg")); }
    [Theory] [InlineData(3, 9, "a 3.png")] [InlineData(3, 12, "a 03.png")] [InlineData(7, 100, "a 007.png")]
    public void PageFileName_PadsToCountDigits(int i, int n, string expected) => Assert.Equal(expected, OutputNaming.PageFileName("a", i, n, "png"));
    [Fact] public void CreatePagesFolder_IsUnique() { var d = TempDir(); var src = Touch(d, "doc.pdf"); Directory.CreateDirectory(Path.Combine(d, "doc (pages)")); Assert.Equal(Path.Combine(d, "doc (pages) 2"), OutputNaming.CreatePagesFolder(src)); Assert.True(Directory.Exists(Path.Combine(d, "doc (pages) 2"))); }
    [Fact] public void ConvertOutput_ClashGetsNumber() { var d = TempDir(); var src = Touch(d, "a.png"); Assert.Equal(Path.Combine(d, "a.jpg"), OutputNaming.ConvertOutput(src, "jpg")); Touch(d, "a.jpg"); Assert.Equal(Path.Combine(d, "a 2.jpg"), OutputNaming.ConvertOutput(src, "jpg")); }
}
