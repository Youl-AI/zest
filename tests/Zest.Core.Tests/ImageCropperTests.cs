using ImageMagick;
using Zest.Core.Jobs;
using Zest.Core.Tests.Fixtures;
using Zest.Core.Tools;

namespace Zest.Core.Tests;

public class ImageCropperTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    [Fact]
    public async Task Crop_ClampsToBounds_AndKeepsFormat()
    {
        var src = FixtureFactory.Image(Dir, "그림.webp", 100, 80);
        var output = await ImageCropper.CropAsync(src, new PixelRect(50, 40, 100, 100), Ctx);
        Assert.Equal(Path.Combine(Dir, "그림 (cropped).webp"), output);
        using var m = new MagickImage(output);
        Assert.Equal(MagickFormat.WebP, m.Format);
        Assert.Equal((50u, 40u), (m.Width, m.Height));
    }

    [Fact]
    public async Task Crop_NegativeOrigin_IsClamped()
    {
        var src = FixtureFactory.Image(Dir, "그림.png", 100, 80);
        var output = await ImageCropper.CropAsync(src, new PixelRect(-20, -10, 50, 50), Ctx);
        using var m = new MagickImage(output);
        Assert.Equal((30u, 40u), (m.Width, m.Height));
    }

    [Theory]
    [InlineData(100, 0, 10, 10)]   // starts at the right edge
    [InlineData(10, 10, 0, 10)]
    [InlineData(10, 10, 10, -5)]
    [InlineData(-50, 0, 20, 20)]   // ends left of the image
    [InlineData(int.MaxValue, 0, 10, 10)]
    public async Task EmptyRect_Throws_AndLeavesNoOutput(int x, int y, int w, int h)
    {
        var src = FixtureFactory.Image(Dir, "그림.png", 100, 80);
        var ex = await Assert.ThrowsAsync<ZestException>(() => ImageCropper.CropAsync(src, new PixelRect(x, y, w, h), Ctx));
        Assert.Equal("자를 영역이 비어 있습니다.", ex.Message);
        Assert.Equal([src], Directory.GetFiles(Dir));
    }

    [Fact]
    public async Task Crop_UsesTheOrientedImage_AndLeavesSourceUntouched()
    {
        var src = FixtureFactory.Image(Dir, "회전.jpg", 100, 50);
        FixtureFactory.AddExifRotate90(src); // displayed as 50 x 100
        var before = File.ReadAllBytes(src);
        var output = await ImageCropper.CropAsync(src, new PixelRect(0, 0, 50, 100), Ctx);
        using var m = new MagickImage(output);
        Assert.Equal((50u, 100u), (m.Width, m.Height));
        Assert.Equal(before, File.ReadAllBytes(src));
    }

    [Fact]
    public async Task ZeroByteImage_Throws()
    {
        var src = Path.Combine(Dir, "빈.png");
        File.WriteAllBytes(src, []);
        var ex = await Assert.ThrowsAsync<ZestException>(() => ImageCropper.CropAsync(src, new PixelRect(0, 0, 5, 5), Ctx));
        Assert.Equal("이미지를 읽을 수 없습니다.", ex.Message);
    }

    [Fact]
    public async Task Crop_OfRotatedSource_HasNoStaleOrientation()
    {
        var src = FixtureFactory.Image(Dir, "회전.jpg", 100, 50);
        FixtureFactory.AddExifRotate90(src);
        var output = await ImageCropper.CropAsync(src, new PixelRect(0, 0, 50, 100), Ctx);
        using var m = new MagickImage(output);
        Assert.Contains(m.Orientation, new[] { Orientation.TopLeft, Orientation.Undefined });
    }

    [Fact]
    public async Task Crop_KeepsLossyQualityHigh()
    {
        var src = FixtureFactory.NoiseImage(Dir, "고화질.jpg", 400, 300, quality: 98);
        var output = await ImageCropper.CropAsync(src, new PixelRect(0, 0, 200, 150), Ctx);
        using var m = new MagickImage(output);
        Assert.True(m.Quality >= 90, $"estimated quality {m.Quality}");
    }

    [Fact]
    public async Task AnimatedSource_IsRefused()
    {
        var src = FixtureFactory.AnimatedWebp(Dir, "움직임.webp");
        var ex = await Assert.ThrowsAsync<ZestException>(() => ImageCropper.CropAsync(src, new PixelRect(0, 0, 10, 10), Ctx));
        Assert.Equal("움직이는 이미지는 아직 지원하지 않습니다.", ex.Message);
        Assert.Equal([src], Directory.GetFiles(Dir));
    }
}
