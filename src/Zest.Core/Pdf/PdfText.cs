using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Zest.Core.Pdf;

/// <summary>Extracts the text layer of a PDF with PdfPig.</summary>
public static class PdfText
{
    /// <summary>One string per page (words separated by spaces, lines in reading order); empty for pages without text.</summary>
    public static IReadOnlyList<string> PageTexts(string pdf) => PageTexts(pdf, CancellationToken.None, null);

    /// <summary>Same, checking <paramref name="ct"/> before each page and reporting (i+1)/n after each page.</summary>
    public static IReadOnlyList<string> PageTexts(string pdf, CancellationToken ct, Action<double>? progress)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(pdf);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ZestException("원본 파일을 열 수 없습니다.");
        }
        if (bytes.Length == 0) throw new ZestException("PDF를 읽을 수 없습니다.");

        try
        {
            using var doc = PdfDocument.Open(bytes);
            var texts = new List<string>(doc.NumberOfPages);
            var n = doc.NumberOfPages;
            for (var i = 1; i <= n; i++)
            {
                ct.ThrowIfCancellationRequested();
                texts.Add(ContentOrderTextExtractor.GetText(doc.GetPage(i)).Trim());
                progress?.Invoke(i / (double)n);
            }
            return texts;
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new ZestException("암호로 보호된 PDF는 열 수 없습니다.");
        }
        catch (Exception e) when (e is not (ZestException or OperationCanceledException))
        {
            throw new ZestException("PDF를 읽을 수 없습니다.");
        }
    }
}
