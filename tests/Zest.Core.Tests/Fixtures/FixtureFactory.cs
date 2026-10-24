using System.Runtime.InteropServices;
using Zest.Core.Catalog;
using Zest.Core.Convert;
using Zest.Core.Office;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Zest.Core.Tests.Fixtures;

internal sealed class MalgunFontResolver : IFontResolver
{
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new("MalgunGothic");

    public byte[]? GetFont(string faceName) =>
        File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "malgun.ttf"));
}

internal static class FixtureFactory
{
    static readonly HashSet<string> AudioExtensions = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma"];

    /// <summary>
    /// Writes a fixture of <paramref name="format"/> named <c>한글 파일 (1).&lt;ext&gt;</c> in <paramref name="dir"/>, using the
    /// tool that can write it: Magick (images), ffmpeg (gif/audio/video, incl. webm), PDFsharp (pdf), Office (docx, doc,
    /// rtf, odt, pptx, ppt, odp, xlsx, xls, ods) or plain text (txt, md, html, csv, tsv). Throws for formats that cannot be made
    /// (heic: Magick here has no HEIC encoder).
    /// </summary>
    public static async Task<string> For(string dir, string format, OfficeHost word, OfficeHost powerPoint, OfficeHost excel)
    {
        var name = $"한글 파일 (1).{format}";
        return FormatCatalog.CategoryOf(format) switch
        {
            FormatCategory.Image => Image(dir, name),
            FormatCategory.Gif or FormatCategory.Audio or FormatCategory.Video => Media(dir, name),
            FormatCategory.Pdf => Pdf(dir, name, 2),
            FormatCategory.Document => await Document(word, dir, name),
            FormatCategory.Slides => await Slides(powerPoint, dir, name),
            FormatCategory.Spreadsheet => await Sheet(excel, dir, name),
            _ => throw new ArgumentException($"No fixture for format '{format}'."),
        };
    }

    /// <summary>
    /// Writes a test clip with ffmpeg (lavfi testsrc 320x240@25 + 440 Hz sine); format follows the extension.
    /// Audio extensions get audio only, gif gets video only.
    /// </summary>
    public static string Media(string dir, string name, int seconds = 2, int width = 320, int height = 240)
    {
        var ffmpeg = FfmpegLocator.Locate() ?? throw new InvalidOperationException("ffmpeg is not on PATH.");
        var path = Path.Combine(dir, name);
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string[] args = ["-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", $"testsrc=size={width}x{height}:rate=25",
            "-f", "lavfi", "-i", "sine=frequency=440", "-t", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (AudioExtensions.Contains(ext)) psi.ArgumentList.Add("-vn");
        else if (ext == "gif") psi.ArgumentList.Add("-an");
        psi.ArgumentList.Add(path);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"ffmpeg fixture failed: {err.Result}");
        return path;
    }

    /// <summary>
    /// Like <see cref="Media"/> with <c>-metadata key=value</c> per tag (mp4/mov/m4a also get <c>-movflags +use_metadata_tags</c>
    /// so QuickTime keys are kept as they are).
    /// </summary>
    public static string TaggedMedia(string dir, string name, IReadOnlyDictionary<string, string> tags, int seconds = 2)
    {
        var ffmpeg = FfmpegLocator.Locate() ?? throw new InvalidOperationException("ffmpeg is not on PATH.");
        var path = Path.Combine(dir, name);
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string[] args = ["-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25",
            "-f", "lavfi", "-i", "sine=frequency=440", "-t", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (AudioExtensions.Contains(ext)) psi.ArgumentList.Add("-vn");
        foreach (var (k, v) in tags) { psi.ArgumentList.Add("-metadata"); psi.ArgumentList.Add($"{k}={v}"); }
        if (ext is "mp4" or "mov" or "m4a") { psi.ArgumentList.Add("-movflags"); psi.ArgumentList.Add("+use_metadata_tags"); }
        psi.ArgumentList.Add(path);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"ffmpeg fixture failed: {err.Result}");
        return path;
    }

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

    /// <summary>Splices a minimal EXIF APP1 segment (Orientation = 6, rotate 90 degrees) into a jpg, in place.</summary>
    public static void AddExifRotate90(string jpgPath)
    {
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 8,
            0x00, 0x01, 0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1, 0x00, 0x06, 0, 0,
            0, 0, 0, 0,
        ];
        var jpg = File.ReadAllBytes(jpgPath);
        File.WriteAllBytes(jpgPath, [.. jpg[..2], .. app1, .. jpg[2..]]);
    }

    /// <summary>
    /// Writes a 64 x 48 jpg carrying an EXIF profile with <paramref name="tags"/>, optionally EXIF GPS (33°29'59.88" N,
    /// 126°31'30" E, 12 m), an XMP packet that repeats the latitude as <c>exif:GPSLatitude</c>, an Orientation other than 1
    /// and the sRGB ICC profile.
    /// </summary>
    public static string JpegWithTags(string dir, string name, IReadOnlyDictionary<ExifTag<string>, string> tags,
        bool withGps = false, bool withXmpGps = false, ushort orientation = 1, bool withIcc = false)
    {
        var path = Path.Combine(dir, name);
        using var image = new MagickImage(new MagickColor("#3366cc"), 64, 48);
        using (var patch = new MagickImage(MagickColors.OrangeRed, 32, 24)) image.Composite(patch, 0, 0, CompositeOperator.Over);
        if (withIcc) image.SetProfile(ColorProfiles.SRGB);
        var exif = new ExifProfile();
        foreach (var (tag, value) in tags) exif.SetValue(tag, value);
        if (orientation != 1)
        {
            exif.SetValue(ExifTag.Orientation, orientation);
            image.Orientation = (Orientation)orientation; // ImageMagick syncs the EXIF value from this on write
        }
        if (withGps)
        {
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLatitude, [new Rational(33, 1), new Rational(29, 1), new Rational(5988, 100)]);
            exif.SetValue(ExifTag.GPSLongitudeRef, "E");
            exif.SetValue(ExifTag.GPSLongitude, [new Rational(126, 1), new Rational(31, 1), new Rational(30, 1)]);
            exif.SetValue(ExifTag.GPSAltitude, new Rational(12, 1));
        }
        if (exif.Values.Any()) image.SetProfile(exif);
        if (withXmpGps)
        {
            var xmp = """
                <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
                <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:exif="http://ns.adobe.com/exif/1.0/" exif:GPSLatitude="33,29.998N"/>
                </rdf:RDF></x:xmpmeta>
                <?xpacket end="w"?>
                """;
            image.SetProfile(new XmpProfile(System.Text.Encoding.UTF8.GetBytes(xmp)));
        }
        image.Write(path, MagickFormat.Jpeg);
        return path;
    }

    /// <summary>One JPEG marker segment: FF, <paramref name="marker"/>, big-endian length, <paramref name="body"/>.</summary>
    public static byte[] JpegSegment(byte marker, byte[] body) =>
        [0xFF, marker, (byte)((body.Length + 2) >> 8), (byte)(body.Length + 2), .. body];

    public static byte[] JpegSegment(byte marker, string asciiBody) => JpegSegment(marker, System.Text.Encoding.ASCII.GetBytes(asciiBody));

    /// <summary>Inserts <paramref name="segments"/> into a jpg, in place, right after SOI and the leading APP0 segments.</summary>
    public static void InsertJpegSegments(string jpgPath, params byte[][] segments)
    {
        var jpg = File.ReadAllBytes(jpgPath);
        var pos = 2;
        while (jpg[pos] == 0xFF && jpg[pos + 1] == 0xE0) pos += 2 + (jpg[pos + 2] << 8 | jpg[pos + 3]);
        File.WriteAllBytes(jpgPath, [.. jpg[..pos], .. segments.SelectMany(s => s), .. jpg[pos..]]);
    }

    /// <summary>Writes a two-frame animated webp.</summary>
    public static string AnimatedWebp(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        using var frames = new MagickImageCollection();
        frames.Add(new MagickImage(MagickColors.Red, 40, 30));
        frames.Add(new MagickImage(MagickColors.Blue, 40, 30));
        foreach (var f in frames) f.AnimationDelay = 10;
        frames.Write(path, MagickFormat.WebP);
        return path;
    }

    /// <summary>Writes a w x h image of random noise (hard to compress) in the format of the extension; <paramref name="quality"/> applies to lossy formats.</summary>
    public static string NoiseImage(string dir, string name, int w, int h, uint quality = 98)
    {
        var path = Path.Combine(dir, name);
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        using var image = new MagickImage(MagickColors.Gray, (uint)w, (uint)h);
        image.AddNoise(NoiseType.Random);
        image.Quality = quality;
        image.Write(path, ext switch
        {
            "jpg" => MagickFormat.Jpeg,
            "png" => MagickFormat.Png,
            "heic" => MagickFormat.Heic,
            "webp" => MagickFormat.WebP,
            "tiff" => MagickFormat.Tiff,
            "bmp" => MagickFormat.Bmp,
            _ => throw new ArgumentException($"Unsupported fixture extension '{ext}'."),
        });
        return path;
    }

    /// <summary>
    /// Writes a one-paragraph document whose format follows the extension. docx/doc/rtf/odt/pdf are made by Word
    /// through <paramref name="word"/> (with <paramref name="password"/> as the open password, if given);
    /// txt (UTF-8, no BOM), md and html are written directly.
    /// </summary>
    public static async Task<string> Document(OfficeHost word, string dir, string name, string text = "안녕 Hello", string? password = null)
    {
        var path = Path.Combine(dir, name);
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        var utf8 = new System.Text.UTF8Encoding(false);
        switch (ext)
        {
            case "txt": File.WriteAllText(path, text, utf8); return path;
            case "md": File.WriteAllText(path, $"# {text}\n\n**{text}**\n", utf8); return path;
            case "html":
                File.WriteAllText(path, $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>t</title></head><body><p>{text}</p></body></html>", utf8);
                return path;
        }
        int format = ext switch
        {
            "docx" => WordFormat.Docx,
            "doc" => WordFormat.Doc97,
            "rtf" => WordFormat.Rtf,
            "odt" => WordFormat.Odt,
            "pdf" => WordFormat.Pdf,
            _ => throw new ArgumentException($"Unsupported fixture extension '{ext}'."),
        };
        await word.RunAsync(app =>
        {
            dynamic docs = app.Documents;
            dynamic? doc = null;
            try
            {
                doc = docs.Add();
                dynamic content = doc.Content;
                content.Text = text;
                Marshal.FinalReleaseComObject(content);
                if (password is null) doc.SaveAs2(FileName: path, FileFormat: format);
                else doc.SaveAs2(FileName: path, FileFormat: format, Password: password);
                return 0;
            }
            finally
            {
                if (doc is not null) { doc.Close(SaveChanges: WordConst.DoNotSaveChanges); Marshal.FinalReleaseComObject(doc); }
                Marshal.FinalReleaseComObject(docs);
            }
        }, TimeSpan.FromMinutes(1), CancellationToken.None);
        return path;
    }

    /// <summary>
    /// Writes a Word 97 .doc that carries a VBA project (a .docm saved as binary .doc). Returns null when Word
    /// refuses programmatic access to VBA projects (Trust Center setting, which tests never change).
    /// </summary>
    public static async Task<string?> MacroDocument(OfficeHost word, string dir, string name)
    {
        var path = Path.Combine(dir, name);
        return await word.RunAsync<string?>(app =>
        {
            dynamic docs = app.Documents;
            dynamic? doc = null;
            try
            {
                doc = docs.Add();
                dynamic project;
                try { project = doc.VBProject; }
                catch (COMException) { return null; } // "Programmatic access to Visual Basic Project is not trusted"
                dynamic components = project.VBComponents;
                dynamic module = components.Add(1); // vbext_ct_StdModule
                dynamic code = module.CodeModule;
                code.AddFromString("Sub Hello()\nEnd Sub\n");
                foreach (var o in new object[] { code, module, components, project }) Marshal.FinalReleaseComObject(o);
                doc.SaveAs2(FileName: path, FileFormat: WordFormat.Doc97);
                return path;
            }
            finally
            {
                if (doc is not null) { doc.Close(SaveChanges: WordConst.DoNotSaveChanges); Marshal.FinalReleaseComObject(doc); }
                Marshal.FinalReleaseComObject(docs);
            }
        }, TimeSpan.FromMinutes(1), CancellationToken.None);
    }

    /// <summary>
    /// Writes a presentation of <paramref name="slides"/> 16:9 title-only slides ("슬라이드 N") through PowerPoint;
    /// format follows the extension (pptx, ppt, odp). <paramref name="password"/> becomes the open password.
    /// </summary>
    public static async Task<string> Slides(OfficeHost ppt, string dir, string name, int slides = 3, string? password = null)
    {
        var path = Path.Combine(dir, name);
        int format = Path.GetExtension(name).TrimStart('.').ToLowerInvariant() switch
        {
            "pptx" => PowerPointFormat.Pptx,
            "ppt" => PowerPointFormat.Ppt,
            "odp" => PowerPointFormat.Odp,
            var e => throw new ArgumentException($"Unsupported fixture extension '{e}'."),
        };
        await ppt.RunAsync(app =>
        {
            dynamic presentations = app.Presentations;
            dynamic? pres = null;
            try
            {
                pres = presentations.Add(WithWindow: MsoConst.False);
                dynamic list = pres.Slides;
                for (var i = 1; i <= slides; i++)
                {
                    dynamic slide = list.Add(i, 11); // ppLayoutTitleOnly
                    dynamic shapes = slide.Shapes;
                    dynamic title = shapes.Item(1);
                    title.TextFrame.TextRange.Text = $"슬라이드 {i}";
                    foreach (var o in new object[] { title, shapes, slide }) Marshal.FinalReleaseComObject(o);
                }
                Marshal.FinalReleaseComObject(list);
                if (password is not null) pres.Password = password;
                pres.SaveAs(path, format);
                return 0;
            }
            finally
            {
                if (pres is not null) { pres.Close(); Marshal.FinalReleaseComObject(pres); }
                Marshal.FinalReleaseComObject(presentations);
            }
        }, TimeSpan.FromMinutes(1), CancellationToken.None);
        return path;
    }

    /// <summary>
    /// Writes a workbook whose first sheet has A1="이름", B2=42; format follows the extension. xlsx, xls and ods are made
    /// by Excel and carry a second sheet ("다른 시트" in A1) that is the active one, so first-sheet-only exports are
    /// observable. csv/tsv are written directly as UTF-8 without BOM. <paramref name="password"/> is the open password.
    /// </summary>
    public static async Task<string> Sheet(OfficeHost excel, string dir, string name, string? password = null)
    {
        var path = Path.Combine(dir, name);
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        var utf8 = new System.Text.UTF8Encoding(false);
        switch (ext)
        {
            case "csv": File.WriteAllText(path, "이름,\r\n,42\r\n", utf8); return path;
            case "tsv": File.WriteAllText(path, "이름\t\r\n\t42\r\n", utf8); return path;
        }
        int format = ext switch
        {
            "xlsx" => ExcelFormat.Xlsx,
            "xls" => ExcelFormat.Xls,
            "ods" => ExcelFormat.Ods,
            _ => throw new ArgumentException($"Unsupported fixture extension '{ext}'."),
        };
        await excel.RunAsync(app =>
        {
            dynamic books = app.Workbooks;
            dynamic? book = null;
            try
            {
                book = books.Add();
                dynamic sheets = book.Worksheets;
                dynamic first = sheets.Item(1);
                first.Range("A1").Value2 = "이름";
                first.Range("B2").Value2 = 42;
                dynamic second = sheets.Add(After: first);
                second.Range("A1").Value2 = "다른 시트";
                second.Activate();
                foreach (var o in new object[] { second, first, sheets }) Marshal.FinalReleaseComObject(o);
                if (password is null) book.SaveAs(Filename: path, FileFormat: format);
                else book.SaveAs(Filename: path, FileFormat: format, Password: password);
                return 0;
            }
            finally
            {
                if (book is not null) { book.Close(SaveChanges: false); Marshal.FinalReleaseComObject(book); }
                Marshal.FinalReleaseComObject(books);
            }
        }, TimeSpan.FromMinutes(1), CancellationToken.None);
        return path;
    }

    /// <summary>A binary .ppt carrying a VBA module; null when PowerPoint refuses programmatic VBA access (never changed by tests).</summary>
    public static Task<string?> MacroPresentation(OfficeHost ppt, string dir, string name)
    {
        var path = Path.Combine(dir, name);
        return ppt.RunAsync<string?>(app =>
        {
            if (!VbaAccessTrusted(app, "PowerPoint")) return null;
            dynamic presentations = app.Presentations;
            dynamic? pres = null;
            try
            {
                pres = presentations.Add(WithWindow: MsoConst.False);
                Marshal.FinalReleaseComObject(pres.Slides.Add(1, 11));
                if (!AddVbaModule(pres)) return null;
                pres.SaveAs(path, PowerPointFormat.Ppt);
                return path;
            }
            finally
            {
                if (pres is not null) { pres.Close(); Marshal.FinalReleaseComObject(pres); }
                Marshal.FinalReleaseComObject(presentations);
            }
        }, TimeSpan.FromMinutes(1), CancellationToken.None);
    }

    /// <summary>A binary .xls carrying a VBA module; null when Excel refuses programmatic VBA access (never changed by tests).</summary>
    public static Task<string?> MacroWorkbook(OfficeHost excel, string dir, string name)
    {
        var path = Path.Combine(dir, name);
        return excel.RunAsync<string?>(app =>
        {
            if (!VbaAccessTrusted(app, "Excel")) return null;
            dynamic books = app.Workbooks;
            dynamic? book = null;
            try
            {
                book = books.Add();
                if (!AddVbaModule(book)) return null;
                book.SaveAs(Filename: path, FileFormat: ExcelFormat.Xls);
                return path;
            }
            finally
            {
                if (book is not null) { book.Close(SaveChanges: false); Marshal.FinalReleaseComObject(book); }
                Marshal.FinalReleaseComObject(books);
            }
        }, TimeSpan.FromMinutes(1), CancellationToken.None);
    }

    /// <summary>
    /// Whether "Trust access to the VBA project object model" is on for <paramref name="appName"/> (read only, never
    /// changed). Checked before touching VBProject: untrusted, PowerPoint and Excel hand back an empty project and then
    /// hang on a modal dialog instead of failing.
    /// </summary>
    static bool VbaAccessTrusted(dynamic app, string appName)
    {
        string version = app.Version; // "16.0"
        foreach (var (root, path) in new[]
        {
            (Microsoft.Win32.Registry.CurrentUser, $@"Software\Policies\Microsoft\Office\{version}\{appName}\Security"),
            (Microsoft.Win32.Registry.CurrentUser, $@"Software\Microsoft\Office\{version}\{appName}\Security"),
            (Microsoft.Win32.Registry.LocalMachine, $@"Software\Microsoft\Office\{version}\{appName}\Security"),
        })
        {
            using var key = root.OpenSubKey(path);
            if (key?.GetValue("AccessVBOM") is int value) return value == 1;
        }
        return false;
    }

    static bool AddVbaModule(dynamic document)
    {
        dynamic project;
        try { project = document.VBProject; }
        catch (COMException) { return false; } // "Programmatic access to Visual Basic Project is not trusted"
        dynamic components = project.VBComponents;
        dynamic module = components.Add(1); // vbext_ct_StdModule
        dynamic code = module.CodeModule;
        code.AddFromString("Sub Hello()\nEnd Sub\n");
        foreach (var o in new object[] { code, module, components, project }) Marshal.FinalReleaseComObject(o);
        return true;
    }

    /// <summary>A white PNG with <paramref name="lines"/> drawn in Malgun Gothic (Magick), for OCR tests.</summary>
    public static string TextImage(string dir, string name, int pointSize = 48, params string[] lines)
    {
        var path = Path.Combine(dir, name);
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "malgun.ttf");
        using var image = new MagickImage(MagickColors.White, (uint)(pointSize * 24), (uint)(pointSize * 2 * (lines.Length + 1)));
        new ImageMagick.Drawing.Drawables()
            .Font(font)
            .FontPointSize(pointSize)
            .FillColor(MagickColors.Black)
            .Text(pointSize, pointSize * 2, string.Join("\n", lines))
            .Draw(image);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    /// <summary>A one-page PDF whose text layer holds <paramref name="text"/> (PDFsharp, Malgun Gothic).</summary>
    public static string TextPdf(string dir, string name, string text)
    {
        GlobalFontSettings.FontResolver ??= new MalgunFontResolver();
        var path = Path.Combine(dir, name);
        using var doc = new PdfDocument();
        var font = new XFont("Malgun Gothic", 24, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
        using (var gfx = XGraphics.FromPdfPage(doc.AddPage()))
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(72, 100));
        doc.Save(path);
        return path;
    }

    /// <summary>
    /// Writes an A4 PDF with <paramref name="pages"/> pages (PDFsharp, test-only). With text, page N shows
    /// "Page N 페이지"; without, only a rectangle (no text layer). A user password makes it encrypted.
    /// </summary>
    public static string Pdf(string dir, string name, int pages, bool withText = true, string? userPassword = null,
        IReadOnlyDictionary<string, string>? info = null)
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
        foreach (var (key, value) in info ?? new Dictionary<string, string>())
        {
            switch (key)
            {
                case "Title": doc.Info.Title = value; break;
                case "Author": doc.Info.Author = value; break;
                case "Subject": doc.Info.Subject = value; break;
                case "Keywords": doc.Info.Keywords = value; break;
                case "Creator": doc.Info.Creator = value; break;
                default: throw new ArgumentException($"No Info setter for '{key}'.");
            }
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
