using ImageMagick;
using Zest.Core.Convert;
using Zest.Core.Jobs;
using Zest.Core.Pdf;
using Zest.Core.Tests.Fixtures;

namespace Zest.Core.Tests;

public class PdfConverterTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);
    static ConversionRouter Router => new([new PdfConverter()]);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    [Fact]
    public async Task ThreePagePdf_ToPng_MakesPagesFolder()
    {
        var src = FixtureFactory.Pdf(Dir, "보고서.pdf", 3);
        var outs = await Router.ConvertAsync(src, "png", Ctx);
        var folder = Assert.Single(outs);
        Assert.Equal(Path.Combine(Dir, "보고서 (pages)"), folder);
        Assert.Equal(new[] { "보고서 1.png", "보고서 2.png", "보고서 3.png" },
            Directory.GetFiles(folder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task OnePagePdf_ToJpg_SingleFile_At200Dpi()
    {
        var src = FixtureFactory.Pdf(Dir, "a.pdf", 1);
        var o = Assert.Single(await Router.ConvertAsync(src, "jpg", Ctx));
        Assert.Equal(Path.Combine(Dir, "a.jpg"), o);
        using var m = new MagickImage(o);
        Assert.InRange((int)m.Width, 1650, 1660); // A4 8.27in x 200
        Assert.Equal(MagickFormat.Jpeg, m.Format);
    }

    [Fact]
    public async Task Pdf_ToTxt_HasPageTextInOrder()
    {
        var src = FixtureFactory.Pdf(Dir, "t.pdf", 2);
        var o = Assert.Single(await Router.ConvertAsync(src, "txt", Ctx));
        Assert.Equal(Path.Combine(Dir, "t.txt"), o);
        var bytes = File.ReadAllBytes(o);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no BOM");
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var i1 = text.IndexOf("Page 1 페이지", StringComparison.Ordinal);
        var i2 = text.IndexOf("Page 2 페이지", StringComparison.Ordinal);
        Assert.True(i1 >= 0, text);
        Assert.True(i2 > i1, text);
    }

    [Fact]
    public async Task Pdf_ToTxt_WithoutTextLayer_Throws_AndLeavesNothing()
    {
        var src = FixtureFactory.Pdf(Dir, "img.pdf", 1, withText: false);
        var e = await Assert.ThrowsAsync<ZestException>(() => Router.ConvertAsync(src, "txt", Ctx));
        Assert.Equal("이 PDF에는 글자 정보가 없습니다. 도구 휠의 '글자 복사'를 쓰세요.", e.Message);
        Assert.False(File.Exists(Path.Combine(Dir, "img.txt")));
    }

    [Theory, InlineData("png"), InlineData("txt")]
    public async Task EncryptedPdf_Throws(string to)
    {
        var src = FixtureFactory.Pdf(Dir, "e.pdf", 1, userPassword: "pw");
        var e = await Assert.ThrowsAsync<ZestException>(() => Router.ConvertAsync(src, to, Ctx));
        Assert.Equal("암호로 보호된 PDF는 열 수 없습니다.", e.Message);
    }

    [Theory, InlineData("png"), InlineData("txt")]
    public async Task CorruptPdf_Throws(string to)
    {
        var src = TestFiles.Touch(Dir, "c.pdf");
        var e = await Assert.ThrowsAsync<ZestException>(() => Router.ConvertAsync(src, to, Ctx));
        Assert.Equal("PDF를 읽을 수 없습니다.", e.Message);
        Assert.Equal(["c.pdf"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task PdfToPng_Cancelled_RemovesPagesFolder()
    {
        var src = FixtureFactory.Pdf(Dir, "big.pdf", 30);
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var ctx = new OperationContext(cts.Token, new CallbackProgress(_ => { if (++calls == 2) cts.Cancel(); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Router.ConvertAsync(src, "png", ctx));
        Assert.Empty(Directory.GetDirectories(Dir));
        Assert.Equal(["big.pdf"], Directory.GetFiles(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task PdfToTxt_Cancelled_LeavesNoTxt()
    {
        var src = FixtureFactory.Pdf(Dir, "big.pdf", 30);
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var ctx = new OperationContext(cts.Token, new CallbackProgress(_ => { if (++calls == 2) cts.Cancel(); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Router.ConvertAsync(src, "txt", ctx));
        Assert.Equal(["big.pdf"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName));
        Assert.True(calls < 30);
    }

    [Theory, InlineData("png"), InlineData("txt")]
    public async Task GarbagePdf_Throws_AndLeavesNothing(string to)
    {
        var src = Path.Combine(Dir, "g.pdf");
        File.WriteAllBytes(src, Enumerable.Range(0, 4096).Select(i => (byte)(i * 31 + 7)).ToArray());
        var e = await Assert.ThrowsAsync<ZestException>(() => Router.ConvertAsync(src, to, Ctx));
        Assert.Equal("PDF를 읽을 수 없습니다.", e.Message);
        Assert.Equal(["g.pdf"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task RenamedPng_AsPdf_Throws()
    {
        var src = Path.Combine(Dir, "r.pdf");
        File.Copy(FixtureFactory.Image(Dir, "x.png"), src);
        await Assert.ThrowsAsync<ZestException>(() => Router.ConvertAsync(src, "png", Ctx));
    }

    [Fact]
    public async Task Render_LongEdgeCappedAt4096()
    {
        var src = FixtureFactory.Pdf(Dir, "a.pdf", 1);
        var png = await PdfRenderer.RenderPagePngAsync(src, 0, dpi: 600, maxEdgePx: 4096, CancellationToken.None);
        using var m = new MagickImage(png);
        Assert.Equal(4096u, m.Height);
    }

    [Fact]
    public async Task PageCount_Works()
    {
        var src = FixtureFactory.Pdf(Dir, "p.pdf", 4);
        Assert.Equal(4, await PdfRenderer.PageCountAsync(src));
    }

    [Fact]
    public async Task ParallelRenders_AreSerialized()
    {
        var src = FixtureFactory.Pdf(Dir, "p.pdf", 3);
        var tasks = Enumerable.Range(0, 8).Select(i => PdfRenderer.RenderPagePngAsync(src, i % 3, 72, 4096, CancellationToken.None));
        foreach (var png in await Task.WhenAll(tasks)) Assert.NotEmpty(png);
    }

    sealed class CallbackProgress(Action<double> a) : IProgress<double>
    {
        public void Report(double v) => a(v);
    }
}
