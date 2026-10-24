using Zest.Core.Tools;

namespace Zest.Core.Tests;

public class TargetSizeTests
{
    [Theory]
    [InlineData("500", SizeUnit.KB, 500_000)]
    [InlineData(" 1.5 ", SizeUnit.MB, 1_500_000)]
    [InlineData("1,5", SizeUnit.MB, 1_500_000)] // comma decimal, like the original
    [InlineData("0,25", SizeUnit.KB, 250)]
    [InlineData("2", SizeUnit.MB, 2_000_000)]
    public void Parse_ReadsDecimalUnits(string text, SizeUnit unit, long bytes) =>
        Assert.Equal(bytes, TargetSize.Parse(text, unit));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("1.000,5")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    public void Parse_RejectsWhatIsNotAPositiveNumber(string text)
    {
        var ex = Assert.Throws<ZestException>(() => TargetSize.Parse(text, SizeUnit.KB));
        Assert.Equal("목표 크기를 숫자로 입력하세요.", ex.Message);
    }

    [Fact]
    public void Parse_RejectsAbsurdlyLargeValues()
    {
        var ex = Assert.Throws<ZestException>(() => TargetSize.Parse("99999999999999999", SizeUnit.MB));
        Assert.Equal("목표 크기를 숫자로 입력하세요.", ex.Message);
    }

    [Fact]
    public void CheckTarget_UsesTheCompressorMessages()
    {
        var tiny = new CompressOptions(SizeGoal.FileSize, TargetBytes: 999);
        Assert.Equal("목표 크기는 1KB 이상이어야 합니다.", Assert.Throws<ZestException>(() => tiny.CheckTarget(50_000)).Message);

        var big = new CompressOptions(SizeGoal.FileSize, TargetBytes: 60_000);
        Assert.Equal("목표가 지금 파일보다 작아야 합니다.", Assert.Throws<ZestException>(() => big.CheckTarget(50_000)).Message);

        new CompressOptions(SizeGoal.FileSize, TargetBytes: 20_000).CheckTarget(50_000);
        new CompressOptions().CheckTarget(10); // preset has no target
    }
}
