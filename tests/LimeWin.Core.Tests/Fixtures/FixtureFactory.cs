using System.Runtime.InteropServices;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Office;
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
