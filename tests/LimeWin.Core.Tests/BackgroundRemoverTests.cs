using ImageMagick;
using ImageMagick.Drawing;
using LimeWin.Core.Jobs;
using LimeWin.Core.Tests.Fixtures;
using LimeWin.Core.Tools;
using Microsoft.ML.OnnxRuntime;

namespace LimeWin.Core.Tests;

public class BackgroundRemoverTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    /// <summary>A white w x h canvas with a red disc of diameter <paramref name="disc"/> in the middle.</summary>
    string RedCircle(string name, int w = 512, int h = 512, int disc = 256)
    {
        var path = Path.Combine(Dir, name);
        using var image = new MagickImage(MagickColors.White, (uint)w, (uint)h);
        new Drawables().FillColor(MagickColors.Red).StrokeColor(MagickColors.Red)
            .Ellipse(w / 2.0, h / 2.0, disc / 2.0, disc / 2.0, 0, 360).Draw(image);
        image.Write(path, Path.GetExtension(name) == ".jpg" ? MagickFormat.Jpeg : MagickFormat.Png);
        return path;
    }

    static byte Alpha(MagickImage image, int x, int y) => image.GetPixels().GetPixel(x, y).GetChannel(3);

    [Fact]
    public async Task Remove_SolidBackgroundCircle_CornersTransparentCenterOpaque()
    {
        Assert.SkipUnless(ModelStore.IsReady, "모델 없음");
        var src = RedCircle("빨간 원.png");
        var before = File.ReadAllBytes(src);

        var output = await BackgroundRemover.RemoveAsync(src, Ctx);

        Assert.Equal(Path.Combine(Dir, "빨간 원 (no background).png"), output);
        Assert.Equal(before, File.ReadAllBytes(src));
        using var result = new MagickImage(output);
        Assert.Equal(MagickFormat.Png, result.Format);
        Assert.Equal((512u, 512u), (result.Width, result.Height));
        Assert.True(result.HasAlpha);
        foreach (var (x, y) in new[] { (2, 2), (509, 2), (2, 509), (509, 509) })
            Assert.True(Alpha(result, x, y) < 20, $"corner ({x},{y}) alpha {Alpha(result, x, y)}");
        Assert.True(Alpha(result, 256, 256) > 235, $"centre alpha {Alpha(result, 256, 256)}");
    }

    [Fact]
    public async Task Remove_ExifRotatedJpg_OutputIsUprightAtOriginalSize()
    {
        Assert.SkipUnless(ModelStore.IsReady, "모델 없음");
        var src = RedCircle("세로.jpg", w: 600, h: 400, disc: 200);
        FixtureFactory.AddExifRotate90(src);

        var output = await BackgroundRemover.RemoveAsync(src, Ctx);

        using var result = new MagickImage(output);
        Assert.Equal((400u, 600u), (result.Width, result.Height));
        Assert.True(Alpha(result, 200, 300) > 235);
    }

    [Fact]
    public async Task Remove_AnimatedImage_RefusedLikeCompress()
    {
        var src = FixtureFactory.AnimatedWebp(Dir, "움직임.webp");

        var ex = await Assert.ThrowsAsync<LimeException>(() => BackgroundRemover.RemoveAsync(src, Ctx));

        Assert.Equal("움직이는 이미지는 아직 지원하지 않습니다.", ex.Message);
        Assert.Single(Directory.GetFiles(Dir));
    }

    [Theory]
    [InlineData("문서.pdf")]
    [InlineData("그림.svg")]
    public async Task Remove_NotARasterImage_Refused(string name)
    {
        var src = Path.Combine(Dir, name);
        File.WriteAllText(src, "x");

        var ex = await Assert.ThrowsAsync<LimeException>(() => BackgroundRemover.RemoveAsync(src, Ctx));

        Assert.Equal("이 파일은 배경을 지울 수 없습니다.", ex.Message);
    }

    [Fact]
    public async Task Remove_Cancelled_NoOutput()
    {
        var src = RedCircle("원.png");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BackgroundRemover.RemoveAsync(src, new OperationContext(cts.Token)));

        Assert.Single(Directory.GetFiles(Dir));
    }

    [Fact]
    public void ToTensor_NormalizesWithImageNetMeanAndStd_InPlanarOrder()
    {
        // Two pixels: pure red, then mid grey.
        byte[] rgb = [255, 0, 0, 128, 128, 128];

        var t = BackgroundRemover.ToTensor(rgb);

        Assert.Equal(6, t.Length);
        float N(byte v, int c) => (v / 255f - BackgroundRemover.Mean[c]) / BackgroundRemover.Std[c];
        Assert.Equal(N(255, 0), t[0], 5); // R plane
        Assert.Equal(N(128, 0), t[1], 5);
        Assert.Equal(N(0, 1), t[2], 5);   // G plane
        Assert.Equal(N(128, 1), t[3], 5);
        Assert.Equal(N(0, 2), t[4], 5);   // B plane
        Assert.Equal(N(128, 2), t[5], 5);
        Assert.Equal((1 - 0.485f) / 0.229f, t[0], 5);
    }

    [Fact]
    public void MaskBytes_AppliesSigmoid()
    {
        var mask = BackgroundRemover.MaskBytes([-20f, 0f, 20f, 2f]);

        Assert.Equal([0, 128, 255, (byte)Math.Round(255 / (1 + Math.Exp(-2)))], mask);
    }

    [Fact]
    public void ChooseDevice_PrefersTensorRtRtxOnGpu_ElseCpu_NeverDirectMl()
    {
        Assert.Equal(1, BackgroundRemover.ChooseDevice([
            ("CPUExecutionProvider", OrtHardwareDeviceType.CPU),
            ("NvTensorRtRtxExecutionProvider", OrtHardwareDeviceType.GPU),
            ("DmlExecutionProvider", OrtHardwareDeviceType.GPU),
        ]));
        Assert.Null(BackgroundRemover.ChooseDevice([
            ("CPUExecutionProvider", OrtHardwareDeviceType.CPU),
            ("DmlExecutionProvider", OrtHardwareDeviceType.GPU),
        ]));
        Assert.Null(BackgroundRemover.ChooseDevice([]));
    }

    [Fact]
    public void IsAvailable_WindowsMlLoadsHere()
    {
        Assert.True(BackgroundRemover.IsAvailable);
    }
}
