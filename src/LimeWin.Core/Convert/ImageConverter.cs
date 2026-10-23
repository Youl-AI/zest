using ImageMagick;
using LimeWin.Core.Catalog;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace LimeWin.Core.Convert;

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
        _ => throw new LimeException("이 변환은 지원하지 않습니다."),
    };

    public Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx) =>
        Task.Run<IReadOnlyList<string>>(() => Convert(source, to, ctx), ctx.Token);

    static string[] Convert(string source, string to, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var from = FormatCatalog.FormatOf(source);
        var output = OutputNaming.ConvertOutput(source, to);

        using (var image = LoadOriented(source, from))
        {
            ctx.Report(0.4);
            ctx.Token.ThrowIfCancellationRequested();
            ctx.Track(output);
            try
            {
                if (to == "pdf") WritePdf(image, output);
                else Encode(image, to, output);
            }
            catch (MagickException)
            {
                throw new LimeException("이미지를 저장하지 못했습니다.");
            }
        }
        ctx.Report(1);
        return [output];
    }

    internal static MagickImage Load(string source, string? from)
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
            throw new LimeException("원본 파일을 열 수 없습니다."); // FileNotFoundException is an IOException
        }
        MagickImage? image = null;
        try
        {
            image = new MagickImage(bytes, settings); // an empty byte array throws ArgumentException
            return image;
        }
        catch (Exception e) when (e is MagickException or ArgumentException)
        {
            image?.Dispose();
            throw new LimeException("이미지를 읽을 수 없습니다.");
        }
    }

    /// <summary>Loads the image and applies its EXIF orientation. The image is disposed if anything after the decode fails.</summary>
    internal static MagickImage LoadOriented(string source, string? from)
    {
        var image = Load(source, from);
        try
        {
            image.AutoOrient();
            return image;
        }
        catch (Exception e) when (e is MagickException or ArgumentException)
        {
            image.Dispose();
            throw new LimeException("이미지를 읽을 수 없습니다.");
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    internal static void Encode(MagickImage image, string to, string output)
    {
        if (to is "jpg" or "bmp") image.ColorAlpha(MagickColors.White);
        if (to is "jpg" or "webp" or "heic" or "avif") image.Quality = 80;
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
            throw new LimeException("이미지를 저장하지 못했습니다.");
        }
        double w = image.Width, h = image.Height;
        var page = builder.AddPage(w, h);
        page.AddPng(png.ToArray(), new PdfRectangle(0, 0, w, h));
    }
}
