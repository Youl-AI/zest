using ImageMagick;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;

namespace LimeWin.Core.Tools;

public enum SizeGoal { Preset, FileSize, Percentage }

public enum SquashLevel { Gentle, Hard }

public enum ResizeCap { Original = 0, Px2560 = 2560, Px1920 = 1920, Px1280 = 1280 }

/// <param name="TargetBytes">Used by <see cref="SizeGoal.FileSize"/> (KB = 1,000 bytes, MB = 1,000,000).</param>
public sealed record CompressOptions(
    SizeGoal Goal = SizeGoal.Preset,
    SquashLevel Level = SquashLevel.Gentle,
    double ShrinkPercent = 50,
    long? TargetBytes = null,
    ResizeCap Cap = ResizeCap.Original)
{
    /// <summary>The size to reach, or null for the preset goal.</summary>
    public long? TargetFor(long originalBytes) => Goal switch
    {
        SizeGoal.FileSize => TargetBytes ?? 0,
        SizeGoal.Percentage => (long)(originalBytes * (1 - Math.Clamp(ShrinkPercent, 1, 99) / 100)),
        _ => null,
    };

    /// <summary>Throws the compressor's own message if the target is unusable for a file of this size (preset: never).</summary>
    public void CheckTarget(long originalBytes)
    {
        if (TargetFor(originalBytes) is not { } t) return;
        if (t < 1000) throw new LimeException("목표 크기는 1KB 이상이어야 합니다.");
        if (t >= originalBytes) throw new LimeException("목표가 지금 파일보다 작아야 합니다.");
    }
}

/// <summary>Makes an image file smaller in its own format; the result is `name (compressed).ext` next to the source.</summary>
public static class ImageCompressor
{
    const int MaxRounds = 8;          // round 0: as is, then metadata dropped (if there is any), then long edge x 0.8 each round
    const int SearchSteps = 7;
    const uint MinLongEdge = 32;

    static readonly HashSet<string> Lossy = ["jpg", "webp", "heic", "avif"];

    public static Task<string> CompressAsync(string source, CompressOptions o, OperationContext ctx) =>
        ToolRunner.RunAsync(ctx, () => Task.Run(() => Compress(source, o, ctx), ctx.Token));

    static string Compress(string source, CompressOptions o, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var format = FormatCatalog.FormatOf(source);
        if (format is null || format is "svg" || FormatCatalog.CategoryOf(format) != FormatCategory.Image || !CapabilityProbe.CanWrite(format))
            throw new LimeException("이 형식은 압축할 수 없습니다.");

        long originalBytes;
        try
        {
            originalBytes = new FileInfo(source).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LimeException("원본 파일을 열 수 없습니다.");
        }

        o.CheckTarget(originalBytes);
        var target = o.TargetFor(originalBytes);

        using var image = ImageConverter.LoadOriented(source, format, singleFrameOnly: true);
        CapLongEdge(image, o.Cap);
        ctx.Report(0.05);

        var bytes = target is { } goal
            ? SearchToTarget(image, format, goal, ctx)
            : Preset(image, format, o, originalBytes, ctx);

        ctx.Token.ThrowIfCancellationRequested();
        var output = OutputNaming.ToolOutput(source, "compressed", format, ctx);
        File.WriteAllBytes(output, bytes);
        ctx.Report(1);
        return output;
    }

    static byte[] Preset(MagickImage image, string format, CompressOptions o, long originalBytes, OperationContext ctx)
    {
        var quality = o.Level == SquashLevel.Hard ? 0.48 : 0.72;
        var bytes = Encode(image, format, quality);
        ctx.Report(0.9);
        if (o.Cap == ResizeCap.Original && bytes.Length >= originalBytes)
            throw new LimeException("더 이상 작게 만들 수 없습니다.");
        return bytes;
    }

    static byte[] SearchToTarget(MagickImage image, string format, long target, OperationContext ctx)
    {
        var lossy = Lossy.Contains(format);
        var encodesPerRound = lossy ? SearchSteps : 1;
        var done = 0;
        long smallest = long.MaxValue;

        var dropped = !HasDroppableMetadata(image);
        for (var round = 0; round < MaxRounds; round++)
        {
            if (round > 0)
            {
                if (!dropped)
                {
                    DropMetadata(image);
                    dropped = true;
                }
                else
                {
                    var (w, h) = (Scale(image.Width), Scale(image.Height));
                    if (Math.Max(w, h) < MinLongEdge) break;
                    image.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true });
                }
            }

            byte[]? best = null;
            if (lossy)
            {
                double lo = 0.05, hi = 0.95;
                for (var step = 0; step < SearchSteps; step++)
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    var mid = (lo + hi) / 2;
                    var bytes = Encode(image, format, mid);
                    smallest = Math.Min(smallest, bytes.Length);
                    ctx.Report(0.05 + 0.85 * ++done / (MaxRounds * encodesPerRound));
                    if (bytes.Length <= target) { best = bytes; lo = mid; }
                    else hi = mid;
                }
            }
            else
            {
                ctx.Token.ThrowIfCancellationRequested();
                var bytes = Encode(image, format, 0.3);
                smallest = Math.Min(smallest, bytes.Length);
                ctx.Report(0.05 + 0.85 * ++done / (MaxRounds * encodesPerRound));
                if (bytes.Length <= target) best = bytes;
            }
            if (best is not null) return best;
        }
        throw new LimeException($"{FormatSize(smallest)}까지 줄였지만 더 작게는 안 됩니다.");
    }

    static bool HasDroppableMetadata(MagickImage image) =>
        image.GetExifProfile() is not null || image.GetXmpProfile() is not null
        || image.GetIptcProfile() is not null || image.Get8BimProfile() is not null;

    /// <summary>Removes EXIF, XMP, IPTC and Photoshop data but keeps the ICC colour profile, so colours do not shift.</summary>
    static void DropMetadata(MagickImage image)
    {
        foreach (var name in new[] { "exif", "xmp", "iptc", "8bim" }) image.RemoveProfile(name);
    }

    static uint Scale(uint edge) => Math.Max(1, (uint)Math.Round(edge * 0.8));

    static void CapLongEdge(MagickImage image, ResizeCap cap)
    {
        if (cap == ResizeCap.Original) return;
        var max = (uint)cap;
        if (Math.Max(image.Width, image.Height) <= max) return;
        image.Resize(new MagickGeometry(max, max)); // fits inside max x max, aspect kept
    }

    /// <summary>Encodes a copy of the image to memory; quality is 0..1 (PNG maps it to a palette size, TIFF is always LZW).</summary>
    static byte[] Encode(MagickImage image, string format, double quality)
    {
        using var copy = (MagickImage)image.Clone();
        switch (format)
        {
            case "png":
                var colors = quality > 0.6 ? 256u : quality > 0.35 ? 128u : 64u;
                copy.Quantize(new QuantizeSettings { Colors = colors });
                copy.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                break;
            case "tiff":
                copy.Settings.Compression = CompressionMethod.LZW;
                break;
            default:
                if (Lossy.Contains(format)) copy.Quality = (uint)Math.Clamp(Math.Round(quality * 100), 1, 100);
                break;
        }
        using var ms = new MemoryStream();
        try
        {
            copy.Write(ms, ImageConverter.MagickFormatOf(format));
        }
        catch (MagickException)
        {
            throw new LimeException("이미지를 저장하지 못했습니다.");
        }
        return ms.ToArray();
    }

    /// <summary>"182 KB" / "1.4 MB" with decimal units like the target field.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1_000 => $"{bytes} B",
        < 1_000_000 => $"{Math.Round(bytes / 1_000.0):0} KB",
        _ => $"{bytes / 1_000_000.0:0.#} MB",
    };
}
