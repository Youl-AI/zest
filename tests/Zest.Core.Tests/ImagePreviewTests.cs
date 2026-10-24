using ImageMagick;
using Zest.Core.Tests.Fixtures;
using Zest.Core.Tools;

namespace Zest.Core.Tests;

public class ImagePreviewTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    [Fact]
    public void Load_DownscalesToTheLongEdge_AndReportsTheSourceSize()
    {
        var src = FixtureFactory.Image(Dir, "큰.png", 400, 200);
        var p = ImagePreview.Load(src, 100);
        Assert.Equal((400, 200), (p.SourceWidth, p.SourceHeight));
        Assert.Equal((100, 50), (p.Width, p.Height));
        Assert.Equal(100 * 50 * 4, p.Bgra.Length);
        Assert.All(Enumerable.Range(0, p.Width * p.Height), i => Assert.Equal(255, p.Bgra[i * 4 + 3])); // opaque source, opaque alpha
    }

    [Fact]
    public void Load_NeverUpscales()
    {
        var src = FixtureFactory.Image(Dir, "작은.jpg", 64, 48);
        var p = ImagePreview.Load(src, 1000);
        Assert.Equal((64, 48), (p.Width, p.Height));
    }

    [Fact]
    public void Load_AppliesExifOrientation_LikeTheCropper()
    {
        var src = FixtureFactory.Image(Dir, "회전.jpg", 100, 50);
        FixtureFactory.AddExifRotate90(src);
        var p = ImagePreview.Load(src, 1000);
        Assert.Equal((50, 100), (p.SourceWidth, p.SourceHeight));
        Assert.Equal((50, 100), (p.Width, p.Height));
    }

    [Fact]
    public void Load_SingleFrameOnly_RefusesAnimation()
    {
        var src = FixtureFactory.AnimatedWebp(Dir, "움짤.webp");
        var ex = Assert.Throws<ZestException>(() => ImagePreview.Load(src, 100, singleFrameOnly: true));
        Assert.Equal("움직이는 이미지는 아직 지원하지 않습니다.", ex.Message);
        var thumb = ImagePreview.Load(src, 100); // thumbnails take the first frame
        Assert.Equal((40, 30), (thumb.Width, thumb.Height));
    }

    [Fact]
    public void Load_CmykJpeg_ShowsTheRgbColours()
    {
        var src = Path.Combine(Dir, "인쇄용.jpg");
        using (var image = new MagickImage(new MagickColor("#3366CC"), 40, 30))
        {
            image.TransformColorSpace(ColorProfiles.SRGB, ColorProfiles.USWebCoatedSWOP); // CMYK with an embedded profile
            image.Write(src, MagickFormat.Jpeg);
        }
        using (var check = new MagickImage(src)) Assert.Equal(ColorSpace.CMYK, check.ColorSpace);

        var p = ImagePreview.Load(src, 100);
        var (b, g, r) = (p.Bgra[0], p.Bgra[1], p.Bgra[2]);
        Assert.InRange(r, 0x33 - 40, 0x33 + 40);
        Assert.InRange(g, 0x66 - 40, 0x66 + 40);
        Assert.InRange(b, 0xCC - 40, 0xCC + 40);
    }

    [Fact]
    public void Load_Unreadable_ThrowsTheConverterMessage()
    {
        var bad = TestFiles.Touch(Dir, "깨짐.png");
        File.WriteAllText(bad, "not an image");
        Assert.Equal("이미지를 읽을 수 없습니다.", Assert.Throws<ZestException>(() => ImagePreview.Load(bad, 100)).Message);
    }
}
