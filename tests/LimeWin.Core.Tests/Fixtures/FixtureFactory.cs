using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace LimeWin.Core.Tests.Fixtures;

internal sealed class MalgunFontResolver : IFontResolver
{
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new("MalgunGothic");

    public byte[]? GetFont(string faceName) =>
        File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "malgun.ttf"));
}

internal static class FixtureFactory
{
    /// <summary>
    /// Writes a w x h image whose format follows the extension. SVG sources are written at half size
    /// (user units) because the converter rasterizes at 192 DPI (2x), so every source yields w x h pixels.
    /// </summary>
    public static string Image(string dir, string name, int w = 64, int h = 48)
    {
        var path = Path.Combine(dir, name);
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        if (ext == "svg")
        {
            File.WriteAllText(path,
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w / 2}\" height=\"{h / 2}\">" +
                $"<rect width=\"{w / 2}\" height=\"{h / 2}\" fill=\"#3366cc\"/></svg>");
            return path;
        }
        using var image = new MagickImage(new MagickColor("#3366cc"), (uint)w, (uint)h);
        using var patch = new MagickImage(MagickColors.OrangeRed, (uint)w / 2, (uint)h / 2);
        image.Composite(patch, 0, 0, CompositeOperator.Over);
        image.Write(path, ext switch
        {
            "jpg" or "jpeg" => MagickFormat.Jpeg,
            "tif" or "tiff" => MagickFormat.Tiff,
            "png" => MagickFormat.Png,
            "webp" => MagickFormat.WebP,
            "heic" => MagickFormat.Heic,
            "avif" => MagickFormat.Avif,
            "bmp" => MagickFormat.Bmp,
            _ => throw new ArgumentException($"Unsupported fixture extension '{ext}'."),
        });
        return path;
    }

    /// <summary>
    /// Writes an A4 PDF with <paramref name="pages"/> pages (PDFsharp, test-only). With text, page N shows
    /// "Page N 페이지"; without, only a rectangle (no text layer). A user password makes it encrypted.
    /// </summary>
    public static string Pdf(string dir, string name, int pages, bool withText = true, string? userPassword = null)
    {
        // The core PDFsharp build has no Windows font access; resolve the one font we need ourselves.
        GlobalFontSettings.FontResolver ??= new MalgunFontResolver();
        var path = Path.Combine(dir, name);
        using var doc = new PdfDocument();
        var font = new XFont("Malgun Gothic", 24, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
        for (var i = 1; i <= pages; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            if (withText) gfx.DrawString($"Page {i} 페이지", font, XBrushes.Black, new XPoint(72, 100));
            else gfx.DrawRectangle(XBrushes.SteelBlue, 72, 100, 200, 100);
        }
        if (userPassword is not null)
        {
            doc.SecuritySettings.UserPassword = userPassword;
            doc.SecuritySettings.OwnerPassword = userPassword + "-owner";
        }
        doc.Save(path);
        return path;
    }
}
