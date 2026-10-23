using LimeWin.Core.Tools;

namespace LimeWin.Core.Tests;

public class ForegroundEstimatorTests
{
    /// <summary>Reference box mean over the window [x - r/2, x - r/2 + r - 1] (cv2.blur's anchor), clipped at the edges.</summary>
    static float[] NaiveBlur(float[] data, int w, int h, int r)
    {
        int before = r / 2, after = r - 1 - before;
        var result = new float[data.Length];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                double sum = 0;
                var count = 0;
                for (var yy = Math.Max(0, y - before); yy <= Math.Min(h - 1, y + after); yy++)
                    for (var xx = Math.Max(0, x - before); xx <= Math.Min(w - 1, x + after); xx++)
                    {
                        sum += data[yy * w + xx];
                        count++;
                    }
                result[y * w + x] = (float)(sum / count);
            }
        return result;
    }

    [Theory]
    [InlineData(37, 23, 6)]
    [InlineData(37, 23, 90)] // window larger than the image
    [InlineData(300, 5, 7)]
    [InlineData(1, 1, 6)]
    public void BoxBlur_MatchesTheNaiveMeanOverTheClippedWindow(int w, int h, int r)
    {
        var rnd = new Random(w * 31 + h);
        var data = Enumerable.Range(0, w * h).Select(_ => (float)rnd.NextDouble()).ToArray();
        var expected = NaiveBlur(data, w, h, r);

        ForegroundEstimator.BoxBlur(data, w, h, r);

        for (var i = 0; i < data.Length; i++) Assert.Equal(expected[i], data[i], 4);
    }

    [Fact]
    public void Estimate_OpaquePixelsKeepTheirColour()
    {
        const int w = 40, h = 30;
        var rnd = new Random(3);
        var rgb = new byte[w * h * 3];
        rnd.NextBytes(rgb);
        var alpha = Enumerable.Repeat(1f, w * h).ToArray();

        Assert.Equal(rgb, ForegroundEstimator.Estimate(rgb, alpha, w, h));
    }

    [Fact]
    public void Estimate_HalfTransparentEdge_GetsTheSubjectsColourNotTheBackgrounds()
    {
        // A red subject on the left half over a green background; a soft 12 px edge where the photo mixes the two.
        const int w = 200, h = 60;
        var rgb = new byte[w * h * 3];
        var alpha = new float[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var a = Math.Clamp((106 - x) / 12f, 0, 1);
                var i = y * w + x;
                alpha[i] = a;
                rgb[i * 3] = (byte)Math.Round(255 * a);           // R from the subject
                rgb[i * 3 + 1] = (byte)Math.Round(255 * (1 - a)); // G from the background
                rgb[i * 3 + 2] = 0;
            }

        var fg = ForegroundEstimator.Estimate(rgb, alpha, w, h);

        var edge = 30 * w + 100; // alpha 0.5
        Assert.Equal(0.5f, alpha[edge], 2);
        Assert.Equal(128, rgb[edge * 3 + 1]); // the photo pixel is half green
        Assert.True(fg[edge * 3] > 200, $"R {fg[edge * 3]}");
        Assert.True(fg[edge * 3 + 1] < 40, $"G {fg[edge * 3 + 1]}");
    }

    [Fact]
    public void ResizeMask_AlignsCornersLikeTheOfficialBilinearUpsample()
    {
        // 2 x 2 -> 3 x 3: corners copy the source corners, the centre is their mean (align_corners=True).
        float[] src = [0f, 1f, 0.5f, 0.25f];

        var m = BackgroundRemover.ResizeMask(src, 2, 2, 3, 3);

        Assert.Equal([0f, 0.5f, 1f, 0.25f, 0.4375f, 0.625f, 0.5f, 0.375f, 0.25f], m);
    }
}
