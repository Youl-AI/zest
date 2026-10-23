using ImageMagick;
using ImageMagick.Formats;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Tests.Fixtures;
using LimeWin.Core.Tools;

namespace LimeWin.Core.Tests;

public class ImageCompressorTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    string NoiseJpg(string name = "사진 (1).jpg") => FixtureFactory.NoiseImage(Dir, name, 2000, 1500);

    [Fact]
    public async Task Preset_Gentle_JpgGetsSmaller_NamedCompressed()
    {
        var src = NoiseJpg();
        var before = File.ReadAllBytes(src);
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(), Ctx);
        Assert.Equal(Path.Combine(Dir, "사진 (1) (compressed).jpg"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(src).Length);
        Assert.Equal(before, File.ReadAllBytes(src));
        using var m = new MagickImage(output);
        Assert.Equal((2000u, 1500u), (m.Width, m.Height));
    }

    [Fact]
    public async Task Preset_Hard_IsSmallerThanGentle()
    {
        var src = NoiseJpg();
        var gentle = await ImageCompressor.CompressAsync(src, new CompressOptions(), Ctx);
        var hard = await ImageCompressor.CompressAsync(src, new CompressOptions(Level: SquashLevel.Hard), Ctx);
        Assert.True(new FileInfo(hard).Length < new FileInfo(gentle).Length);
    }

    [Fact]
    public async Task TargetSize_HitsTarget()
    {
        var src = NoiseJpg();
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.FileSize, TargetBytes: 200_000), Ctx);
        var size = new FileInfo(output).Length;
        Assert.InRange(size, 1, 200_000);
        Assert.True(size > 100_000, $"search should land close to the target, got {size}");
    }

    [Fact]
    public async Task Percentage50_RoughlyHalvesOrLess()
    {
        var src = NoiseJpg();
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.Percentage, ShrinkPercent: 50), Ctx);
        Assert.True(new FileInfo(output).Length <= new FileInfo(src).Length / 2);
    }

    [Fact]
    public async Task TargetBelow1KB_Throws()
    {
        var src = NoiseJpg();
        var ex = await Assert.ThrowsAsync<LimeException>(() =>
            ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.FileSize, TargetBytes: 999), Ctx));
        Assert.Equal("목표 크기는 1KB 이상이어야 합니다.", ex.Message);
    }

    [Fact]
    public async Task FileSizeGoalWithoutTarget_Throws()
    {
        var src = NoiseJpg();
        var ex = await Assert.ThrowsAsync<LimeException>(() => ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.FileSize), Ctx));
        Assert.Equal("목표 크기는 1KB 이상이어야 합니다.", ex.Message);
    }

    [Fact]
    public async Task TargetNotBelowOriginal_Throws()
    {
        var src = NoiseJpg();
        var size = new FileInfo(src).Length;
        var ex = await Assert.ThrowsAsync<LimeException>(() =>
            ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.FileSize, TargetBytes: size), Ctx));
        Assert.Equal("목표가 지금 파일보다 작아야 합니다.", ex.Message);
    }

    [Fact]
    public async Task UnreachableTarget_ReportsSmallestSize_AndLeavesNoOutput()
    {
        var src = FixtureFactory.NoiseImage(Dir, "잡음.jpg", 1600, 1600);
        var ex = await Assert.ThrowsAsync<LimeException>(() =>
            ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.FileSize, TargetBytes: 1000), Ctx));
        Assert.Matches(@"^\d+ (B|KB)까지 줄였지만 더 작게는 안 됩니다\.$", ex.Message);
        Assert.Equal([src], Directory.GetFiles(Dir));
    }

    [Fact]
    public async Task Cap1280_LongEdgeIs1280()
    {
        var src = NoiseJpg();
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(Cap: ResizeCap.Px1280), Ctx);
        using var m = new MagickImage(output);
        Assert.Equal((1280u, 960u), (m.Width, m.Height));
    }

    [Fact]
    public async Task Cap_NeverEnlarges()
    {
        var src = FixtureFactory.NoiseImage(Dir, "작음.jpg", 300, 200);
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(Cap: ResizeCap.Px1280), Ctx);
        using var m = new MagickImage(output);
        Assert.Equal((300u, 200u), (m.Width, m.Height));
    }

    [Fact]
    public async Task AlreadyTinyPng_PresetThrows()
    {
        var src = Path.Combine(Dir, "점.png");
        using (var img = new MagickImage(MagickColors.Red, 1, 1)) img.Write(src, MagickFormat.Png);
        var ex = await Assert.ThrowsAsync<LimeException>(() => ImageCompressor.CompressAsync(src, new CompressOptions(), Ctx));
        Assert.Equal("더 이상 작게 만들 수 없습니다.", ex.Message);
        Assert.Equal([src], Directory.GetFiles(Dir));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task Preset_OtherFormats_KeepFormatAndShrink(string ext)
    {
        var src = FixtureFactory.NoiseImage(Dir, $"그림.{ext}", 600, 400, quality: 100);
        await AssertShrinks(src, ext);
    }

    [Fact]
    public async Task Preset_Tiff_UsesLzw()
    {
        // Uncompressed TIFF (Magick's default) of a flat picture: LZW wins big.
        var src = FixtureFactory.Image(Dir, "그림.tiff", 600, 400);
        await AssertShrinks(src, "tiff");
    }

    async Task AssertShrinks(string src, string ext)
    {
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(), Ctx);
        Assert.Equal(Path.Combine(Dir, $"그림 (compressed).{ext}"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(src).Length);
        using var m = new MagickImage(output);
        Assert.Equal((600u, 400u), (m.Width, m.Height));
    }

    [Fact]
    public async Task TargetSize_Png_ReachesTarget()
    {
        var src = FixtureFactory.NoiseImage(Dir, "잡음.png", 800, 600);
        var size = new FileInfo(src).Length;
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.FileSize, TargetBytes: size / 4), Ctx);
        Assert.True(new FileInfo(output).Length <= size / 4);
    }

    [Fact]
    public async Task Svg_IsNotCompressible()
    {
        var src = FixtureFactory.Image(Dir, "벡터.svg");
        var ex = await Assert.ThrowsAsync<LimeException>(() => ImageCompressor.CompressAsync(src, new CompressOptions(), Ctx));
        Assert.Equal("이 형식은 압축할 수 없습니다.", ex.Message);
    }

    [Fact]
    public async Task ZeroByteImage_ThrowsLimeException()
    {
        var src = Path.Combine(Dir, "빈.jpg");
        File.WriteAllBytes(src, []);
        var ex = await Assert.ThrowsAsync<LimeException>(() => ImageCompressor.CompressAsync(src, new CompressOptions(), Ctx));
        Assert.Equal("이미지를 읽을 수 없습니다.", ex.Message);
    }

    [Fact]
    public async Task Preset_Heic_UsesQuality()
    {
        Assert.SkipUnless(CapabilityProbe.CanWrite("heic"), "Magick here has no HEIC encoder");
        var src = FixtureFactory.NoiseImage(Dir, "사진.heic", 600, 400);
        var gentle = await ImageCompressor.CompressAsync(src, new CompressOptions(), Ctx);
        var hard = await ImageCompressor.CompressAsync(src, new CompressOptions(Level: SquashLevel.Hard), Ctx);
        Assert.True(new FileInfo(hard).Length < new FileInfo(gentle).Length);
    }

    [Fact]
    public async Task TargetSize_Heic_SearchesQuality()
    {
        Assert.SkipUnless(CapabilityProbe.CanWrite("heic"), "Magick here has no HEIC encoder");
        var src = FixtureFactory.NoiseImage(Dir, "사진.heic", 600, 400);
        var target = new FileInfo(src).Length / 2;
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.FileSize, TargetBytes: target), Ctx);
        Assert.True(new FileInfo(output).Length <= target);
        using var m = new MagickImage(output);
        Assert.Equal((600u, 400u), (m.Width, m.Height)); // quality alone was enough, no shrinking
    }

    [Fact]
    public async Task TargetSize_DropsExifButKeepsIcc()
    {
        var src = Path.Combine(Dir, "프로필.jpg");
        using (var img = new MagickImage(MagickColors.Gray, 300, 300))
        {
            img.AddNoise(NoiseType.Random);
            img.SetProfile(ColorProfiles.SRGB);
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.ImageDescription, new string('x', 60_000)); // too big for the target on its own
            img.SetProfile(exif);
            img.Write(src, MagickFormat.Jpeg);
        }
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.FileSize, TargetBytes: 30_000), Ctx);
        using var m = new MagickImage(output);
        Assert.InRange(new FileInfo(output).Length, 1, 30_000);
        Assert.Null(m.GetExifProfile());
        Assert.NotNull(m.GetColorProfile());
        Assert.Equal((300u, 300u), (m.Width, m.Height)); // dropping metadata was enough
    }

    [Fact]
    public async Task AnimatedSource_IsRefused()
    {
        var src = FixtureFactory.AnimatedWebp(Dir, "움직임.webp");
        var ex = await Assert.ThrowsAsync<LimeException>(() => ImageCompressor.CompressAsync(src, new CompressOptions(Cap: ResizeCap.Px1280), Ctx));
        Assert.Equal("움직이는 이미지는 아직 지원하지 않습니다.", ex.Message);
        Assert.Equal([src], Directory.GetFiles(Dir));
    }

    [Fact]
    public async Task ExifRotatedSource_OutputHasNoStaleOrientation()
    {
        var src = FixtureFactory.NoiseImage(Dir, "회전.jpg", 600, 400);
        FixtureFactory.AddExifRotate90(src);
        var output = await ImageCompressor.CompressAsync(src, new CompressOptions(), Ctx);
        using var m = new MagickImage(output);
        Assert.Equal((400u, 600u), (m.Width, m.Height));
        Assert.Contains(m.Orientation, new[] { Orientation.TopLeft, Orientation.Undefined });
    }

    sealed class CancelAfter(CancellationTokenSource cts, int reports) : IProgress<double>
    {
        int _n;
        public void Report(double value) { if (++_n >= reports) cts.Cancel(); }
    }

    [Fact]
    public async Task CancelDuringTargetSearch_LeavesNoOutput()
    {
        var src = NoiseJpg();
        using var cts = new CancellationTokenSource();
        var ctx = new OperationContext(cts.Token, new CancelAfter(cts, 3)); // the first progress plus two attempts
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageCompressor.CompressAsync(src, new CompressOptions(SizeGoal.FileSize, TargetBytes: 20_000), ctx));
        Assert.Equal([src], Directory.GetFiles(Dir));
    }

    [Fact]
    public void TargetFor_UsesDecimalUnitsAndClampsPercent()
    {
        Assert.Equal(200_000, new CompressOptions(SizeGoal.FileSize, TargetBytes: 200_000).TargetFor(1_000_000));
        Assert.Equal(250_000, new CompressOptions(SizeGoal.Percentage, ShrinkPercent: 75).TargetFor(1_000_000));
        Assert.Equal(10_000, new CompressOptions(SizeGoal.Percentage, ShrinkPercent: 150).TargetFor(1_000_000));
        Assert.Null(new CompressOptions().TargetFor(1_000_000));
    }
}
