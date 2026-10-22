using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace LimeWin.Core.Pdf;

/// <summary>Extracts the text layer of a PDF with PdfPig.</summary>
public static class PdfText
{
    /// <summary>One string per page (words separated by spaces, lines in reading order); empty for pages without text.</summary>
    public static IReadOnlyList<string> PageTexts(string pdf)
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

        try
        {
            using var doc = PdfDocument.Open(bytes);
            var texts = new List<string>(doc.NumberOfPages);
            foreach (var page in doc.GetPages())
                texts.Add(ContentOrderTextExtractor.GetText(page).Trim());
            return texts;
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new LimeException("암호로 보호된 PDF는 열 수 없습니다.");
        }
        catch (Exception e) when (e is not LimeException)
        {
            throw new LimeException("PDF를 읽을 수 없습니다.");
        }
    }
}
