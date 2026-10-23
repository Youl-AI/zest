using ImageMagick;
using LimeWin.Core.Jobs;
using LimeWin.Core.Pdf;
using LimeWin.Core.Tests.Fixtures;
using LimeWin.Core.Tools;

namespace LimeWin.Core.Tests;

public class PdfToolsTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static int PageCount(string pdf) => PdfText.PageTexts(pdf).Count;

    string Solid(string name, MagickColor color)
    {
        var path = Path.Combine(Dir, name);
        using var image = new MagickImage(color, 80, 60);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    static async Task<string> CenterColor(string pdf, int index0)
    {
        var png = await PdfRenderer.RenderPagePngAsync(pdf, index0, 72, 4096, CancellationToken.None);
        using var m = new MagickImage(png);
        var c = m.GetPixels().GetPixel((int)m.Width / 2, (int)m.Height / 2).ToColor()!;
        var (r, g, b) = (c.R, c.G, c.B);
        return r > 200 && g < 60 && b < 60 ? "red" : g > 100 && r < 60 && b < 60 ? "green" : b > 200 && r < 60 && g < 60 ? "blue" : $"{r},{g},{b}";
    }

    [Fact]
    public async Task ImagesToPdf_OrderPreserved()
    {
        var red = Solid("r.png", MagickColors.Red);
        var green = Solid("g.png", new MagickColor("#00ff00"));
        var blue = Solid("b.png", MagickColors.Blue);
        var o = await PdfTools.ImagesToPdfAsync([red, green, blue], Ctx);
        Assert.Equal(Path.Combine(Dir, "r (merged).pdf"), o);
        Assert.Equal(3, PageCount(o));
        Assert.Equal(["red", "green", "blue"], new[] { await CenterColor(o, 0), await CenterColor(o, 1), await CenterColor(o, 2) });
    }

    [Fact]
    public async Task ImagesToPdf_SingleImage_OnePage()
    {
        var o = await PdfTools.ImagesToPdfAsync([FixtureFactory.Image(Dir, "one.jpg")], Ctx);
        Assert.Equal(Path.Combine(Dir, "one (merged).pdf"), o);
        Assert.Equal(1, PageCount(o));
    }

    [Fact]
    public async Task ImagesToPdf_UnreadableImage_Throws_AndLeavesNothing()
    {
        var good = FixtureFactory.Image(Dir, "ok.png");
        var bad = TestFiles.Touch(Dir, "bad.png");
        var e = await Assert.ThrowsAsync<LimeException>(() => PdfTools.ImagesToPdfAsync([good, bad], Ctx));
        Assert.Equal("이미지를 읽을 수 없습니다.", e.Message);
        Assert.Equal(["bad.png", "ok.png"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task ImagesToPdf_Cancelled_LeavesNoPdf()
    {
        var images = Enumerable.Range(0, 10).Select(i => FixtureFactory.Image(Dir, $"i{i}.png")).ToArray();
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var ctx = new OperationContext(cts.Token, new CallbackProgress(_ => { if (++calls == 2) cts.Cancel(); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfTools.ImagesToPdfAsync(images, ctx));
        Assert.DoesNotContain(Directory.GetFiles(Dir), f => f.EndsWith(".pdf"));
    }

    [Fact]
    public async Task Merge_PageCountIsSum_NamedMerged()
    {
        var a = FixtureFactory.Pdf(Dir, "가.pdf", 2);
        var b = FixtureFactory.Pdf(Dir, "나.pdf", 3);
        var o = await PdfTools.MergeAsync([a, b], Ctx);
        Assert.Equal(Path.Combine(Dir, "가 (merged).pdf"), o);
        Assert.Equal(5, PageCount(o));
    }

    [Fact]
    public async Task Merge_PreservesPageOrder()
    {
        var a = FixtureFactory.Pdf(Dir, "a.pdf", 2);
        var b = FixtureFactory.Pdf(Dir, "b.pdf", 3);
        var texts = PdfText.PageTexts(await PdfTools.MergeAsync([b, a], Ctx));
        string[] expected = ["Page 1", "Page 2", "Page 3", "Page 1", "Page 2"];
        Assert.Equal(expected.Length, texts.Count);
        for (var i = 0; i < expected.Length; i++) Assert.Contains($"{expected[i]} 페이지", texts[i]);
    }

    [Fact]
    public async Task Merge_DoesNotModifySources()
    {
        var a = FixtureFactory.Pdf(Dir, "a.pdf", 1);
        var b = FixtureFactory.Pdf(Dir, "b.pdf", 1);
        var before = (File.ReadAllBytes(a), File.ReadAllBytes(b));
        await PdfTools.MergeAsync([a, b], Ctx);
        Assert.Equal(before.Item1, File.ReadAllBytes(a));
        Assert.Equal(before.Item2, File.ReadAllBytes(b));
    }

    [Fact]
    public async Task Merge_EncryptedInput_Throws()
    {
        var a = FixtureFactory.Pdf(Dir, "a.pdf", 1);
        var e = FixtureFactory.Pdf(Dir, "e.pdf", 1, userPassword: "pw");
        var ex = await Assert.ThrowsAsync<LimeException>(() => PdfTools.MergeAsync([a, e], Ctx));
        Assert.Equal("암호로 보호된 PDF는 열 수 없습니다.", ex.Message);
        Assert.Equal(["a.pdf", "e.pdf"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Merge_CorruptInput_Throws()
    {
        var a = FixtureFactory.Pdf(Dir, "a.pdf", 1);
        var c = TestFiles.Touch(Dir, "c.pdf");
        var ex = await Assert.ThrowsAsync<LimeException>(() => PdfTools.MergeAsync([a, c], Ctx));
        Assert.Equal("PDF를 읽을 수 없습니다.", ex.Message);
        Assert.Equal(["a.pdf", "c.pdf"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Merge_Cancelled_LeavesNoPdf()
    {
        var pdfs = Enumerable.Range(0, 10).Select(i => FixtureFactory.Pdf(Dir, $"p{i}.pdf", 1)).ToArray();
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var ctx = new OperationContext(cts.Token, new CallbackProgress(_ => { if (++calls == 2) cts.Cancel(); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfTools.MergeAsync(pdfs, ctx));
        Assert.Equal(10, Directory.GetFiles(Dir).Length);
    }

    [Fact]
    public async Task Split_TwelvePages_ZeroPadded()
    {
        var o = await PdfTools.SplitAsync(FixtureFactory.Pdf(Dir, "s.pdf", 12), Ctx);
        Assert.Equal(Path.Combine(Dir, "s (pages)"), o);
        Assert.Equal("s 01.pdf", Path.GetFileName(Directory.GetFiles(o).Order().First()));
        Assert.Equal(12, Directory.GetFiles(o).Length);
    }

    [Fact]
    public async Task Split_EachFileIsOnePageWithMatchingText()
    {
        var o = await PdfTools.SplitAsync(FixtureFactory.Pdf(Dir, "s.pdf", 3), Ctx);
        var files = Directory.GetFiles(o).Order().ToArray();
        for (var i = 0; i < 3; i++)
        {
            var texts = PdfText.PageTexts(files[i]);
            Assert.Single(texts);
            Assert.Contains($"Page {i + 1} 페이지", texts[0]);
        }
    }

    [Fact]
    public async Task Split_Encrypted_Throws_AndLeavesNothing()
    {
        var e = FixtureFactory.Pdf(Dir, "e.pdf", 2, userPassword: "pw");
        var ex = await Assert.ThrowsAsync<LimeException>(() => PdfTools.SplitAsync(e, Ctx));
        Assert.Equal("암호로 보호된 PDF는 열 수 없습니다.", ex.Message);
        Assert.Equal(["e.pdf"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_Corrupt_Throws_AndLeavesNothing()
    {
        var c = TestFiles.Touch(Dir, "c.pdf");
        var ex = await Assert.ThrowsAsync<LimeException>(() => PdfTools.SplitAsync(c, Ctx));
        Assert.Equal("PDF를 읽을 수 없습니다.", ex.Message);
        Assert.Equal(["c.pdf"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_Cancelled_RemovesFolder()
    {
        var src = FixtureFactory.Pdf(Dir, "big.pdf", 30);
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var ctx = new OperationContext(cts.Token, new CallbackProgress(_ => { if (++calls == 2) cts.Cancel(); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfTools.SplitAsync(src, ctx));
        Assert.Empty(Directory.GetDirectories(Dir));
        Assert.Equal(["big.pdf"], Directory.GetFiles(Dir).Select(Path.GetFileName));
    }

    sealed class CallbackProgress(Action<double> a) : IProgress<double>
    {
        public void Report(double v) => a(v);
    }
}
