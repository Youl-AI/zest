using ImageMagick;
using Zest.Core.Catalog;
using Zest.Core.Jobs;
using Zest.Core.Output;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Zest.Core.Convert;

public sealed class ImageConverter : IConverter
{
    public bool Handles(string from, string to) =>
        from != to
        && FormatCatalog.CategoryOf(from) == FormatCategory.Image
        && (to == "pdf" || (to != "svg" && FormatCatalog.CategoryOf(to) == FormatCategory.Image));

    internal static MagickFormat MagickFormatOf(string format) => format switch
    {
        "jpg" => MagickFormat.Jpeg,
        "png" => MagickFormat.Png,
        "webp" => MagickFormat.WebP,
        "heic" => MagickFormat.Heic,
        "tiff" => MagickFormat.Tiff,
        "avif" => MagickFormat.Avif,
        "bmp" => MagickFormat.Bmp,
        "svg" => MagickFormat.Svg,
        _ => throw new ZestException("이 변환은 지원하지 않습니다."),
    };

    public Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx) =>
        ctx.InSlotAsync(EngineGates.Image, () => Task.Run<IReadOnlyList<string>>(() => Convert(source, to, ctx), ctx.Token));

    static string[] Convert(string source, string to, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var from = FormatCatalog.FormatOf(source);
        string output;

        using (var image = LoadOriented(source, from))
        {
            ctx.Report(0.4);
            ctx.Token.ThrowIfCancellationRequested();
            output = OutputNaming.ConvertOutput(source, to, ctx); // reserved here; written over below
            try
            {
                if (to == "pdf") WritePdf(image, output);
                else Encode(image, to, output);
            }
            catch (MagickException)
            {
                throw new ZestException("이미지를 저장하지 못했습니다.");
            }
        }
        ctx.Report(1);
        return [output];
    }

    /// <param name="singleFrameOnly">Tools that write one image back (compress, crop) refuse animated or multi-page sources instead of flattening them.</param>
    internal static MagickImage Load(string source, string? from, bool singleFrameOnly = false)
    {
        // Read from bytes so the content, not the (possibly wrong) extension, decides the decoder.
        var settings = new MagickReadSettings();
        if (from == "svg")
        {
            settings.Format = MagickFormat.Svg;
            settings.Density = new Density(192);
        }
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ZestException("원본 파일을 열 수 없습니다."); // FileNotFoundException is an IOException
        }
        try
        {
            if (singleFrameOnly && FrameCount(bytes, settings) > 1)
                throw new ZestException("움직이는 이미지는 아직 지원하지 않습니다.");
            return new MagickImage(bytes, settings); // an empty byte array throws ArgumentException
        }
        catch (Exception e) when (e is MagickException or ArgumentException)
        {
            throw new ZestException("이미지를 읽을 수 없습니다.");
        }
    }

    static int FrameCount(byte[] bytes, MagickReadSettings settings)
    {
        try
        {
            using var frames = new MagickImageCollection();
            frames.Ping(bytes, settings);
            return frames.Count;
        }
        catch (Exception e) when (e is MagickException or ArgumentException)
        {
            return 1; // the real decode reports the problem
        }
    }

    /// <summary>Loads the image and applies its EXIF orientation. The image is disposed if anything after the decode fails.</summary>
    internal static MagickImage LoadOriented(string source, string? from, bool singleFrameOnly = false)
    {
        var image = Load(source, from, singleFrameOnly);
        try
        {
            image.AutoOrient();
            return image;
        }
        catch (Exception e) when (e is MagickException or ArgumentException)
        {
            image.Dispose();
            throw new ZestException("이미지를 읽을 수 없습니다.");
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    internal static void Encode(MagickImage image, string to, string output, uint lossyQuality = 80)
    {
        if (to is "jpg" or "bmp") image.ColorAlpha(MagickColors.White);
        if (to is "jpg" or "webp" or "heic" or "avif") image.Quality = lossyQuality;
        if (to == "png") image.Settings.SetDefine(MagickFormat.Png, "compression-level", "6");
        image.Write(output, MagickFormatOf(to));
    }

    static void WritePdf(MagickImage image, string output)
    {
        var builder = new PdfDocumentBuilder();
        AddPdfPage(builder, image);
        File.WriteAllBytes(output, builder.Build());
    }

    /// <summary>Adds one page sized to the image (1 px = 1 pt) with the image filling it.</summary>
    internal static void AddPdfPage(PdfDocumentBuilder builder, MagickImage image)
    {
        using var png = new MemoryStream();
        try
        {
            image.Write(png, MagickFormat.Png);
        }
        catch (MagickException)
        {
            throw new ZestException("이미지를 저장하지 못했습니다.");
        }
        double w = image.Width, h = image.Height;
        var page = builder.AddPage(w, h);
        page.AddPng(png.ToArray(), new PdfRectangle(0, 0, w, h));
    }
}
