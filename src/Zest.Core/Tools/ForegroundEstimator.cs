namespace Zest.Core.Tools;

/// <summary>
/// Foreground colour estimation for a cut-out: a half-transparent edge pixel of a photo is a mix of subject and background,
/// so keeping its colour leaves a fringe of the old background. This recovers the subject's own colour there with the
/// "blur fusion" estimator BiRefNet's <c>refine_foreground</c> uses (Photoroom's fast foreground estimation): two passes,
/// box radius 90 then 6. Opaque pixels keep their colour exactly.
/// </summary>
internal static class ForegroundEstimator
{
    const int CoarseRadius = 90;
    const int FineRadius = 6;
    const float Epsilon = 1e-5f;

    /// <summary>
    /// The estimated foreground colours (interleaved RGB bytes) of <paramref name="rgb"/> under <paramref name="alpha"/> (0..1).
    /// Works one channel at a time; peak extra memory is about 20 bytes per pixel.
    /// </summary>
    public static byte[] Estimate(byte[] rgb, float[] alpha, int width, int height)
    {
        var n = width * height;
        var coarse = (float[])alpha.Clone();
        BoxBlur(coarse, width, height, CoarseRadius);
        var fine = (float[])alpha.Clone();
        BoxBlur(fine, width, height, FineRadius);

        var result = new byte[n * 3];
        var f = new float[n];
        var b = new float[n];
        for (var c = 0; c < 3; c++)
        {
            // Pass 1, with the image as both the first foreground and background guess:
            // F1 = blur(I a) / blur(a) + a (I - a blur(F) - (1 - a) blur(B)), B1 = blur(I (1 - a)) / blur(1 - a).
            Parallel.For(0, n, i =>
            {
                var image = rgb[i * 3 + c] / 255f;
                f[i] = image * alpha[i];
                b[i] = image * (1 - alpha[i]);
            });
            BoxBlur(f, width, height, CoarseRadius);
            BoxBlur(b, width, height, CoarseRadius);
            Parallel.For(0, n, i =>
            {
                var a = alpha[i];
                var blurredF = f[i] / (coarse[i] + Epsilon);
                var blurredB = b[i] / (1 - coarse[i] + Epsilon);
                var image = rgb[i * 3 + c] / 255f;
                f[i] = Math.Clamp(blurredF + a * (image - a * blurredF - (1 - a) * blurredB), 0, 1) * a; // F1 a, ready to blur
                b[i] = blurredB * (1 - a);                                                                 // B1 (1 - a)
            });

            // Pass 2: the same with F1 and B1 and the small radius.
            BoxBlur(f, width, height, FineRadius);
            BoxBlur(b, width, height, FineRadius);
            Parallel.For(0, n, i =>
            {
                var a = alpha[i];
                var image = rgb[i * 3 + c] / 255f;
                var value = a >= 1
                    ? image // exact for opaque pixels (the formula gives the same up to rounding)
                    : Math.Clamp(Estimate(image, a, f[i] / (fine[i] + Epsilon), b[i] / (1 - fine[i] + Epsilon)), 0, 1);
                result[i * 3 + c] = (byte)MathF.Round(value * 255);
            });
        }
        return result;
    }

    static float Estimate(float image, float a, float blurredF, float blurredB) =>
        blurredF + a * (image - a * blurredF - (1 - a) * blurredB);

    /// <summary>
    /// In place: each value becomes the mean over the r x r window from <c>-r/2</c> to <c>r - 1 - r/2</c> around it (cv2.blur's
    /// anchor), with the window clipped at the image edges and the mean taken over the pixels inside.
    /// </summary>
    internal static void BoxBlur(float[] data, int width, int height, int r)
    {
        int before = r / 2, after = r - 1 - before;

        // Rows: prefix sums.
        Parallel.For(0, height, () => new double[width + 1], (y, _, prefix) =>
        {
            var row = data.AsSpan(y * width, width);
            for (var x = 0; x < width; x++) prefix[x + 1] = prefix[x] + row[x];
            for (var x = 0; x < width; x++)
            {
                int lo = Math.Max(0, x - before), hi = Math.Min(width - 1, x + after);
                row[x] = (float)((prefix[hi + 1] - prefix[lo]) / (hi - lo + 1));
            }
            return prefix;
        }, _ => { });

        // Columns, in strips: running column sums over a sliding window of rows; the original rows still needed after they
        // are overwritten are kept in a ring of before + 1 rows.
        const int Strip = 256;
        Parallel.For(0, (width + Strip - 1) / Strip, s =>
        {
            int x0 = s * Strip, x1 = Math.Min(width, x0 + Strip), span = x1 - x0;
            var sums = new double[span];
            var ring = new float[(before + 1) * span];
            for (var y = 0; y <= Math.Min(after, height - 1); y++)
                for (var x = 0; x < span; x++) sums[x] += data[y * width + x0 + x];

            for (var y = 0; y < height; y++)
            {
                if (y > 0)
                {
                    var incoming = y + after;
                    if (incoming < height)
                        for (var x = 0; x < span; x++) sums[x] += data[incoming * width + x0 + x];
                    var outgoing = y - before - 1;
                    if (outgoing >= 0)
                    {
                        var slot = outgoing % (before + 1) * span;
                        for (var x = 0; x < span; x++) sums[x] -= ring[slot + x];
                    }
                }
                int count = Math.Min(height - 1, y + after) - Math.Max(0, y - before) + 1;
                var mine = y % (before + 1) * span; // the slot just freed by the outgoing row
                for (var x = 0; x < span; x++)
                {
                    var i = y * width + x0 + x;
                    ring[mine + x] = data[i];
                    data[i] = (float)(sums[x] / count);
                }
            }
        });
    }
}
