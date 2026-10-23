using ImageMagick;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Writer;

namespace LimeWin.Core.Tools;

/// <summary>Wheel tools for PDFs: images to one PDF, merge several PDFs, split one PDF into single-page files.</summary>
public static class PdfTools
{
    /// <summary>One page per image, in the given order; result is `first (merged).pdf` next to the first image.</summary>
    public static Task<string> ImagesToPdfAsync(IReadOnlyList<string> imagesInOrder, OperationContext ctx) =>
        ToolRunner.RunAsync(ctx, () => ctx.InSlotAsync(EngineGates.Image, () => Task.Run(() =>
        {
            RequireInputs(imagesInOrder);
            var builder = new PdfDocumentBuilder();
            for (var i = 0; i < imagesInOrder.Count; i++)
            {
                ctx.Token.ThrowIfCancellationRequested();
                using var image = ImageConverter.LoadOriented(imagesInOrder[i], FormatCatalog.FormatOf(imagesInOrder[i]));
                ImageConverter.AddPdfPage(builder, image);
                ctx.Report((i + 1) / (double)imagesInOrder.Count);
            }
            return WriteOutput(imagesInOrder[0], builder.Build(), ctx);
        }, ctx.Token)));

    /// <summary>All pages of all PDFs, in the given order; result is `first (merged).pdf` next to the first PDF.</summary>
    public static Task<string> MergeAsync(IReadOnlyList<string> pdfsInOrder, OperationContext ctx) =>
        ToolRunner.RunAsync(ctx, () => ctx.InSlotAsync(EngineGates.Image, () => Task.Run(() =>
        {
            RequireInputs(pdfsInOrder);
            var files = new List<byte[]>(pdfsInOrder.Count);
            for (var i = 0; i < pdfsInOrder.Count; i++)
            {
                ctx.Token.ThrowIfCancellationRequested();
                var bytes = ReadPdf(pdfsInOrder[i]);
                Guard(() => { using var doc = PdfDocument.Open(bytes); return doc.NumberOfPages; }); // reject encrypted/corrupt early
                files.Add(bytes);
                ctx.Report(0.9 * (i + 1) / pdfsInOrder.Count);
            }
            ctx.Token.ThrowIfCancellationRequested();
            var merged = Guard(() => PdfMerger.Merge(files));
            return WriteOutput(pdfsInOrder[0], merged, ctx);
        }, ctx.Token)));

    /// <summary>Writes each page as `stem NN.pdf` into a new `stem (pages)` folder next to the PDF; returns the folder.</summary>
    public static Task<string> SplitAsync(string pdf, OperationContext ctx) =>
        ToolRunner.RunAsync(ctx, () => ctx.InSlotAsync(EngineGates.Image, () => Task.Run(() =>
        {
            ctx.Token.ThrowIfCancellationRequested();
            var bytes = ReadPdf(pdf);
            return Guard(() =>
            {
                using var doc = PdfDocument.Open(bytes);
                var n = doc.NumberOfPages;
                if (n < 1) throw new LimeException("PDF를 읽을 수 없습니다.");

                var folder = OutputNaming.CreatePagesFolder(pdf, ctx);
                var stem = OutputNaming.Stem(pdf);
                for (var i = 1; i <= n; i++)
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    var builder = new PdfDocumentBuilder();
                    builder.AddPage(doc, i);
                    File.WriteAllBytes(Path.Combine(folder, OutputNaming.PageFileName(stem, i, n, "pdf")), builder.Build());
                    ctx.Report(i / (double)n);
                }
                return folder;
            });
        }, ctx.Token)));

    static void RequireInputs(IReadOnlyList<string> inputs)
    {
        if (inputs.Count == 0) throw new LimeException("선택한 파일이 없습니다.");
    }

    static string WriteOutput(string first, byte[] pdf, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var output = OutputNaming.ToolOutput(first, "merged", "pdf", ctx);
        File.WriteAllBytes(output, pdf);
        ctx.Report(1);
        return output;
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

    /// <summary>Maps PdfPig failures to the Korean messages PdfConverter uses.</summary>
    static T Guard<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new LimeException("암호로 보호된 PDF는 열 수 없습니다.");
        }
        catch (Exception e) when (e is not (LimeException or OperationCanceledException or IOException or UnauthorizedAccessException))
        {
            throw new LimeException("PDF를 읽을 수 없습니다.");
        }
    }
}
