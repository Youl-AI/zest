using Zest.Core.Jobs;
using Zest.Core.Pdf;
using Zest.Core.Tests.Fixtures;
using Zest.Core.Tools;

namespace Zest.Core.Tests;

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

        await Assert.ThrowsAsync<ZestException>(() => OcrService.RecognizeAsync(txt, Ctx));
    }

    [Fact]
    public async Task SaveText_WritesUtf8WithoutBom_NextToSource_AndNeverOverwrites()
    {
        var source = Path.Combine(Dir, "스캔.png");
        File.WriteAllBytes(source, [1]);

        var first = await OcrService.SaveTextAsync(source, "안녕하세요\n둘째 줄", Ctx);
        var second = await OcrService.SaveTextAsync(source, "again", Ctx);

        Assert.Equal(Path.Combine(Dir, "스캔 (text).txt"), first);
        Assert.Equal(Path.Combine(Dir, "스캔 (text) 2.txt"), second);
        Assert.Equal([.. System.Text.Encoding.UTF8.GetBytes("안녕하세요\r\n둘째 줄")], File.ReadAllBytes(first));
    }

    sealed class Recorder(Action<double>? onReport = null) : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value)
        {
            Values.Add(value);
            onReport?.Invoke(value);
        }
    }

    [Fact]
    public async Task Pdf_ReportsProgressPerPage()
    {
        RequireOcr();
        var pdf = FixtureFactory.Pdf(Dir, "three.pdf", 3);
        var progress = new Recorder();

        await OcrService.RecognizeAsync(pdf, new OperationContext(CancellationToken.None, progress));

        Assert.Equal([1 / 3.0, 2 / 3.0, 1.0], progress.Values);
    }

    [Fact]
    public async Task Pdf_CancelledAfterFirstPage_Throws_AndStopsReporting()
    {
        RequireOcr();
        var pdf = FixtureFactory.Pdf(Dir, "three.pdf", 3);
        using var cts = new CancellationTokenSource();
        var progress = new Recorder(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OcrService.RecognizeAsync(pdf, new OperationContext(cts.Token, progress)));

        Assert.Equal([1 / 3.0], progress.Values);
    }

    [Fact]
    public void TextLayerThreshold_Is20NonWhitespaceCharacters()
    {
        Assert.False(OcrService.UseTextLayer(new string('가', 19)));
        Assert.True(OcrService.UseTextLayer(new string('가', 20)));
        Assert.False(OcrService.UseTextLayer(new string('가', 19) + " \n\t "));
        Assert.True(OcrService.UseTextLayer("가 나 다 라 마 바 사 아 자 차 카 타 파 하 거 너 더 러 머 버"));
    }

    [Fact]
    public async Task Pdf_TextLayerOfExactly20Chars_IsUsedAsIs()
    {
        RequireOcr();
        var pdf = FixtureFactory.TextPdf(Dir, "twenty.pdf", "abcdefghij klmnopqrst");

        Assert.Equal("abcdefghij klmnopqrst", await OcrService.RecognizeAsync(pdf, Ctx));
    }

    [Fact]
    public void WindowsNewlines_NormalizesEveryStyleToCrLf()
    {
        Assert.Equal("a\r\nb\r\nc\r\nd", OcrService.WindowsNewlines("a\nb\r\nc\rd"));
    }

    [Fact]
    public void IsAvailable_MatchesInstalledLanguages()
    {
        Assert.Equal(OcrService.InstalledLanguages.Count > 0, OcrService.IsAvailable);
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
