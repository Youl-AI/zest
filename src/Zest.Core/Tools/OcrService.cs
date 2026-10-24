using System.Runtime.InteropServices;
using System.Text;
using ImageMagick;
using Zest.Core.Catalog;
using Zest.Core.Convert;
using Zest.Core.Jobs;
using Zest.Core.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Zest.Core.Tools;

/// <summary>Reads text out of images and PDFs with the Windows OCR engine (<c>Windows.Media.Ocr</c>; languages come from the installed language packs).</summary>
public static class OcrService
{
    /// <summary>A PDF page whose text layer has at least this many non-whitespace characters is used as it is.</summary>
    internal const int MinTextLayerChars = 20;
    const int PdfDpi = 300;
    const string Preferred = "ko";

    static readonly Lazy<OcrEngine?> Engine = new(CreateEngine);

    /// <summary>True when at least one OCR language is installed. Cheap: it does not build the engine (that waits for the first recognition).</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                return OcrEngine.AvailableRecognizerLanguages.Count > 0;
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or TypeLoadException)
            {
                return false;
            }
        }
    }

    /// <summary>BCP-47 tags of the installed OCR languages (empty when none).</summary>
    public static IReadOnlyList<string> InstalledLanguages
    {
        get
        {
            try
            {
                return [.. OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag)];
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or TypeLoadException)
            {
                return [];
            }
        }
    }

    /// <summary>The Korean engine when installed (it reads Latin text too), else the user-profile engine, else any installed one.</summary>
    static OcrEngine? CreateEngine()
    {
        try
        {
            var languages = OcrEngine.AvailableRecognizerLanguages;
            var ko = languages.FirstOrDefault(l => l.LanguageTag.StartsWith(Preferred, StringComparison.OrdinalIgnoreCase));
            if (ko is not null && OcrEngine.TryCreateFromLanguage(ko) is { } korean) return korean;
            if (OcrEngine.TryCreateFromUserProfileLanguages() is { } profile) return profile;
            return languages.Select(OcrEngine.TryCreateFromLanguage).FirstOrDefault(e => e is not null);
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or TypeLoadException)
        {
            return null;
        }
    }

    /// <summary>Recognized text of an image or a PDF; empty when nothing was found. Pages of a PDF are separated by a blank line.</summary>
    public static async Task<string> RecognizeAsync(string path, OperationContext ctx)
    {
        var engine = Engine.Value ?? throw new ZestException("이 컴퓨터에는 글자 인식 언어가 설치되어 있지 않습니다.");
        ctx.Token.ThrowIfCancellationRequested();
        var format = FormatCatalog.FormatOf(path);
        switch (FormatCatalog.CategoryOf(format ?? ""))
        {
            case FormatCategory.Pdf:
                return await RecognizePdfAsync(engine, path, ctx);
            case FormatCategory.Image:
                ctx.Report(0.1);
                using (var bitmap = await Task.Run(() =>
                {
                    using var image = ImageConverter.LoadOriented(path, format);
                    return ToBitmap(image);
                }, ctx.Token))
                {
                    var text = await RecognizeBitmapAsync(engine, bitmap, ctx.Token);
                    ctx.Report(1);
                    return text;
                }
            default:
                throw new ZestException("이 파일에서는 글자를 읽을 수 없습니다.");
        }
    }

    /// <summary>
    /// One file: its text. Several files: a "── name ──" line before each file's text, a blank line between files; a file
    /// without text says so under its heading. Empty when no file had text.
    /// </summary>
    public static string Combine(IReadOnlyList<(string Path, string Text)> results)
    {
        if (results.All(r => r.Text.Length == 0)) return "";
        if (results.Count == 1) return results[0].Text;
        return string.Join("\n\n", results.Select(r =>
            $"── {Output.OutputNaming.DisplayName(r.Path)} ──\n{(r.Text.Length > 0 ? r.Text : "(글자를 찾지 못했습니다.)")}"));
    }

    /// <summary>Writes <paramref name="text"/> as UTF-8 (no BOM) to <c>name (text).txt</c> next to <paramref name="source"/>; returns the path.</summary>
    public static Task<string> SaveTextAsync(string source, string text, OperationContext ctx) =>
        ToolRunner.RunAsync(ctx, () => Task.Run(() =>
        {
            ctx.Token.ThrowIfCancellationRequested();
            var output = Output.OutputNaming.ToolOutput(source, "text", "txt", ctx);
            File.WriteAllText(output, WindowsNewlines(text), new UTF8Encoding(false));
            ctx.Report(1);
            return output;
        }, ctx.Token));

    static async Task<string> RecognizePdfAsync(OcrEngine engine, string pdf, OperationContext ctx)
    {
        var layers = await Task.Run(() => PdfText.PageTexts(pdf, ctx.Token, null), ctx.Token);
        var pages = new List<string>(layers.Count);
        for (var i = 0; i < layers.Count; i++)
        {
            ctx.Token.ThrowIfCancellationRequested();
            if (UseTextLayer(layers[i]))
            {
                pages.Add(layers[i]);
            }
            else
            {
                var png = await PdfRenderer.RenderPagePngAsync(pdf, i, PdfDpi, (int)OcrEngine.MaxImageDimension, ctx.Token);
                using var bitmap = await Task.Run(() =>
                {
                    using var image = new MagickImage(png);
                    return ToBitmap(image);
                }, ctx.Token);
                pages.Add(await RecognizeBitmapAsync(engine, bitmap, ctx.Token));
            }
            ctx.Report((i + 1) / (double)layers.Count);
        }
        // Blank pages (no text layer and nothing recognized) are skipped; the rest are joined by exactly one blank line.
        return string.Join("\n\n", pages.Select(p => p.Trim()).Where(p => p.Length > 0));
    }

    internal static bool UseTextLayer(string layerText) => layerText.Count(c => !char.IsWhiteSpace(c)) >= MinTextLayerChars;

    /// <summary>The text with Windows line endings (CRLF), whatever line endings it had.</summary>
    public static string WindowsNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    static async Task<string> RecognizeBitmapAsync(OcrEngine engine, SoftwareBitmap bitmap, CancellationToken ct)
    {
        var result = await engine.RecognizeAsync(bitmap).AsTask(ct);
        return string.Join("\n", result.Lines.Select(l => JoinWords(l.Words.Select(w => w.Text))));
    }

    /// <summary>Synchronous: scales the image to the engine's limit and copies its pixels into a Bgra8 bitmap the caller disposes.</summary>
    static SoftwareBitmap ToBitmap(MagickImage image)
    {
        var max = (int)OcrEngine.MaxImageDimension;
        if (Math.Max(image.Width, image.Height) > max)
            image.Resize(new MagickGeometry((uint)max, (uint)max)); // fits inside max x max, keeps the aspect ratio
        image.ColorAlpha(MagickColors.White); // OCR sees no transparency
        var bgra = image.GetPixels().ToByteArray(PixelMapping.BGRA) ?? throw new ZestException("이미지를 읽을 수 없습니다.");
        using var writer = new DataWriter();
        writer.WriteBytes(bgra);
        return SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Bgra8,
            (int)image.Width, (int)image.Height, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>Words with a space between them, except between two Han/kana characters (Chinese and Japanese are written without spaces).</summary>
    internal static string JoinWords(IEnumerable<string> words)
    {
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            if (sb.Length > 0 && !(IsUnspaced(sb[^1]) && IsUnspaced(word[0]))) sb.Append(' ');
            sb.Append(word);
        }
        return sb.ToString();
    }

    static bool IsUnspaced(char c) =>
        c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿';
}
