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
        try
        {
            using var image = Load(source, from);
            ctx.Report(0.4);
            image.AutoOrient();
            ctx.Token.ThrowIfCancellationRequested();

            if (to == "pdf") WritePdf(image, output);
            else Encode(image, to, output);
        }
        catch (MagickException)
        {
            throw new LimeException("이미지를 읽을 수 없습니다.");
        }
        ctx.Report(1);
        return [output];
    }

    static MagickImage Load(string source, string? from)
    {
        // Read from bytes so the content, not the (possibly wrong) extension, decides the decoder.
        var settings = new MagickReadSettings();
        if (from == "svg")
        {
            settings.Format = MagickFormat.Svg;
            settings.Density = new Density(192);
        }
        return new MagickImage(File.ReadAllBytes(source), settings);
    }

    static void Encode(MagickImage image, string to, string output)
    {
        if (to is "jpg" or "bmp") image.ColorAlpha(MagickColors.White);
        if (to is "jpg" or "webp" or "heic" or "avif") image.Quality = 80;
        if (to == "png") image.Settings.SetDefine(MagickFormat.Png, "compression-level", "6");
        image.Write(output, MagickFormatOf(to));
    }

    static void WritePdf(MagickImage image, string output)
    {
        using var png = new MemoryStream();
        image.Write(png, MagickFormat.Png);

        // 1 px = 1 pt; the image fills the page.
        double w = image.Width, h = image.Height;
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(w, h);
        page.AddPng(png.ToArray(), new PdfRectangle(0, 0, w, h));
        File.WriteAllBytes(output, builder.Build());
    }
}
