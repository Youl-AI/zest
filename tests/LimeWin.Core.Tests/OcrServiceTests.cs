using LimeWin.Core.Jobs;
using LimeWin.Core.Pdf;
using LimeWin.Core.Tests.Fixtures;
using LimeWin.Core.Tools;

namespace LimeWin.Core.Tests;

public class OcrServiceTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static void RequireOcr()
    {
        if (!OcrService.IsAvailable) Assert.Skip("No Windows OCR language is installed on this machine.");
    }

    [Fact]
    public async Task Image_KoreanText_Recognized()
    {
        RequireOcr();
        var image = FixtureFactory.TextImage(Dir, "글.png", 48, "안녕하세요 2026");

        var text = await OcrService.RecognizeAsync(image, Ctx);

        Assert.Contains("안녕하세요", text);
        Assert.Contains("2026", text);
    }

    [Fact]
    public async Task Image_MixedKoreanAndEnglish_KeepsWordsAndLines()
    {
        RequireOcr();
        var image = FixtureFactory.TextImage(Dir, "mix.png", 40, "Lime 파일 변환 tool", "Quarterly report 2026");

        var text = await OcrService.RecognizeAsync(image, Ctx);

        Assert.Contains("파일 변환", text);
        Assert.Contains("Quarterly report", text);
        Assert.Contains('\n', text);
    }

    [Fact]
    public async Task Image_LargerThanEngineLimit_IsScaledDownAndStillRead()
    {
        RequireOcr();
        var small = FixtureFactory.TextImage(Dir, "big.png", 48, "안녕하세요 2026");
        using (var image = new ImageMagick.MagickImage(small))
        {
            image.Extent(12000, 3000, ImageMagick.Gravity.Northwest, ImageMagick.MagickColors.White);
            image.Write(small);
        }

        var text = await OcrService.RecognizeAsync(small, Ctx);

        Assert.Contains("2026", text);
    }

    [Fact]
    public async Task Pdf_WithTextLayer_UsesLayerNotOcr()
    {
        RequireOcr();
        var pdf = FixtureFactory.TextPdf(Dir, "layer.pdf", "The quick brown fox jumps over 열두 개의 게으른 개");

        var text = await OcrService.RecognizeAsync(pdf, Ctx);

        Assert.Equal(PdfText.PageTexts(pdf)[0], text);
    }

    [Fact]
    public async Task ScannedPdf_FallsBackToOcr()
    {
        RequireOcr();
        var image = FixtureFactory.TextImage(Dir, "scan.png", 48, "안녕하세요 2026");
        var pdf = await PdfTools.ImagesToPdfAsync([image], Ctx);
        Assert.Equal("", PdfText.PageTexts(pdf)[0]);

        var text = await OcrService.RecognizeAsync(pdf, Ctx);

        Assert.Contains("안녕하세요", text);
        Assert.Contains("2026", text);
    }

    [Fact]
    public async Task Pdf_PagesAreSeparatedByBlankLine()
    {
        RequireOcr();
        var a = FixtureFactory.TextImage(Dir, "a.png", 48, "첫째 쪽 입니다");
        var b = FixtureFactory.TextImage(Dir, "b.png", 48, "둘째 쪽 입니다");
        var pdf = await PdfTools.ImagesToPdfAsync([a, b], Ctx);

        var text = await OcrService.RecognizeAsync(pdf, Ctx);

        Assert.Contains("\n\n", text);
        Assert.True(text.IndexOf("첫째", StringComparison.Ordinal) < text.IndexOf("둘째", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancelled_Throws()
    {
        RequireOcr();
        var image = FixtureFactory.TextImage(Dir, "c.png", 48, "안녕하세요");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OcrService.RecognizeAsync(image, new OperationContext(cts.Token)));
    }

    [Fact]
    public async Task UnsupportedFile_Throws()
    {
        RequireOcr();
        var txt = Path.Combine(Dir, "a.txt");
        File.WriteAllText(txt, "x");

        await Assert.ThrowsAsync<LimeException>(() => OcrService.RecognizeAsync(txt, Ctx));
    }

    [Fact]
    public async Task SaveText_WritesUtf8WithoutBom_NextToSource_AndNeverOverwrites()
    {
        var source = Path.Combine(Dir, "스캔.png");
        File.WriteAllBytes(source, [1]);

        var first = await OcrService.SaveTextAsync(source, "안녕하세요", Ctx);
        var second = await OcrService.SaveTextAsync(source, "again", Ctx);

        Assert.Equal(Path.Combine(Dir, "스캔 (text).txt"), first);
        Assert.Equal(Path.Combine(Dir, "스캔 (text) 2.txt"), second);
        Assert.Equal([.. System.Text.Encoding.UTF8.GetBytes("안녕하세요")], File.ReadAllBytes(first));
    }

    [Fact]
    public void Combine_SingleFileIsPlain_SeveralGetHeadings_NothingFoundIsEmpty()
    {
        Assert.Equal("가", OcrService.Combine([(@"C:\a\x.png", "가")]));
        Assert.Equal("── x.png ──\n가\n\n── y.pdf ──\n(글자를 찾지 못했습니다.)",
            OcrService.Combine([(@"C:\a\x.png", "가"), (@"C:\a\y.pdf", "")]));
        Assert.Equal("", OcrService.Combine([(@"C:\a\x.png", ""), (@"C:\a\y.pdf", "")]));
    }

    [Fact]
    public void JoinWords_SpacesWordsButNotHanOrKana()
    {
        Assert.Equal("안녕하세요 2026 abc", OcrService.JoinWords(["안녕하세요", "2026", "abc"]));
        Assert.Equal("日本語テキスト", OcrService.JoinWords(["日本語", "テキスト"]));
    }
}
