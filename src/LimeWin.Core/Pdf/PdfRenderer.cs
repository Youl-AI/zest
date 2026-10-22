using LimeWin.Core.Jobs;
using LimeWin.Core.Output;
using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;

namespace LimeWin.Core.Pdf;

/// <summary>Renders PDF pages with PDFium (via PDFtoImage). PDFium is not thread-safe, so every call is serialized.</summary>
public static class PdfRenderer
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<int> PageCountAsync(string pdf)
    {
        var bytes = ReadPdf(pdf);
        return await Locked(CancellationToken.None, () => Conversion.GetPageCount(bytes));
    }

    /// <summary>
    /// Renders every page. If <paramref name="outDirOrFile"/> is an existing directory each page becomes
    /// `stem N.ext` inside it, otherwise the (single) page is written to that file path.
    /// Returns the written file paths. The caller tracks <paramref name="outDirOrFile"/> for cleanup.
    /// </summary>
    public static async Task<IReadOnlyList<string>> RenderPagesAsync(
        string pdf, string outDirOrFile, string ext, int dpi, int maxEdgePx, OperationContext ctx)
    {
        var bytes = ReadPdf(pdf);
        var count = await Locked(ctx.Token, () => Conversion.GetPageCount(bytes));
        var asFolder = Directory.Exists(outDirOrFile);
        if (!asFolder && count != 1) throw new ArgumentException("A single output file needs a one-page PDF.", nameof(outDirOrFile));

        var stem = OutputNaming.Stem(pdf);
        var written = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            ctx.Token.ThrowIfCancellationRequested();
            var encoded = await Locked(ctx.Token, () => Encode(Render(bytes, i, dpi, maxEdgePx), ext));
            var path = asFolder
                ? Path.Combine(outDirOrFile, OutputNaming.PageFileName(stem, i + 1, count, ext))
                : outDirOrFile;
            await File.WriteAllBytesAsync(path, encoded, ctx.Token);
            written.Add(path);
            ctx.Report((i + 1) / (double)count);
        }
        return written;
    }

    public static async Task<byte[]> RenderPagePngAsync(string pdf, int index0, int dpi, int maxEdgePx, CancellationToken ct)
    {
        var bytes = ReadPdf(pdf);
        return await Locked(ct, () => Encode(Render(bytes, index0, dpi, maxEdgePx), "png"));
    }

    static byte[] ReadPdf(string pdf)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(pdf);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LimeException("원본 파일을 열 수 없습니다.");
        }
        if (bytes.Length == 0) throw new LimeException("PDF를 읽을 수 없습니다.");
        return bytes;
    }

    static SKBitmap Render(byte[] pdf, int index0, int dpi, int maxEdgePx)
    {
        var size = Conversion.GetPageSize(pdf, index0); // points (1/72 in)
        var w = size.Width * dpi / 72.0;
        var h = size.Height * dpi / 72.0;
        var scale = Math.Min(1.0, maxEdgePx / Math.Max(w, h));
        var options = new RenderOptions(
            Dpi: dpi,
            Width: Math.Max(1, (int)Math.Round(w * scale)),
            Height: Math.Max(1, (int)Math.Round(h * scale)),
            BackgroundColor: SKColors.White);
        return Conversion.ToImage(pdf, index0, options: options);
    }

    static byte[] Encode(SKBitmap bitmap, string ext)
    {
        using (bitmap)
        using (var image = SKImage.FromBitmap(bitmap))
        {
            var format = ext == "jpg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
            using var data = image.Encode(format, ext == "jpg" ? 80 : 100)
                ?? throw new LimeException("이미지를 저장하지 못했습니다.");
            return data.ToArray();
        }
    }

    static async Task<T> Locked<T>(CancellationToken ct, Func<T> work)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    return work();
                }
                catch (PdfPasswordProtectedException)
                {
                    throw new LimeException("암호로 보호된 PDF는 열 수 없습니다.");
                }
                catch (Exception e) when (e is not (LimeException or OperationCanceledException))
                {
                    throw new LimeException("PDF를 읽을 수 없습니다."); // PdfException and anything PDFium/Skia throws on garbage
                }
            }, CancellationToken.None);
        }
        finally
        {
            Gate.Release();
        }
    }
}
