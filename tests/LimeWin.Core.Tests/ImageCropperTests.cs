using ImageMagick;
using LimeWin.Core.Jobs;
using LimeWin.Core.Tests.Fixtures;
using LimeWin.Core.Tools;

namespace LimeWin.Core.Tests;

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
        var ex = await Assert.ThrowsAsync<LimeException>(() => ImageCropper.CropAsync(src, new PixelRect(x, y, w, h), Ctx));
        Assert.Equal("자를 영역이 비어 있습니다.", ex.Message);
        Assert.Equal([src], Directory.GetFiles(Dir));
    }

    [Fact]
    public async Task Crop_UsesTheOrientedImage_AndLeavesSourceUntouched()
    {
        var src = FixtureFactory.Image(Dir, "회전.jpg", 100, 50);
        // Splice a minimal EXIF APP1 segment (Orientation = 6, rotate 90 degrees) in after the SOI marker; displayed as 50 x 100.
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 8,
            0x00, 0x01, 0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1, 0x00, 0x06, 0, 0,
            0, 0, 0, 0,
        ];
        var jpg = File.ReadAllBytes(src);
        File.WriteAllBytes(src, [.. jpg[..2], .. app1, .. jpg[2..]]);
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
        var ex = await Assert.ThrowsAsync<LimeException>(() => ImageCropper.CropAsync(src, new PixelRect(0, 0, 5, 5), Ctx));
        Assert.Equal("이미지를 읽을 수 없습니다.", ex.Message);
    }
}
