using System.Security.Cryptography;
using ImageMagick;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Tests.Fixtures;
using UglyToad.PdfPig;

namespace LimeWin.Core.Tests;

public class ImageConverterTests : IDisposable
{
    static readonly string[] Sources = ["jpg", "png", "webp", "heic", "tiff", "avif", "bmp", "svg"];
    static readonly string[] Targets = ["jpg", "png", "webp", "heic", "tiff", "avif", "bmp", "pdf"];

    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);
    static ConversionRouter Router => new([new ImageConverter()]);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    // Sources whose fixture Magick cannot write (heic/avif without an encoder) are skipped as well as targets.
    public static TheoryData<string, string> Routes()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in Sources)
        {
            if (from is "heic" or "avif" && !CapabilityProbe.CanWrite(from)) continue;
            foreach (var to in Targets)
            {
                if (to == from) continue;
                if (to is "heic" or "avif" && !CapabilityProbe.CanWrite(to)) continue;
                data.Add(from, to);
            }
        }
        return data;
    }

    static string Sha256(string path) => System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Theory, MemberData(nameof(Routes))]
    public async Task Converts_AndOutputDecodes(string from, string to)
    {
        var src = FixtureFactory.Image(Dir, $"그림 (1).{from}");
        var before = Sha256(src);
        var outs = await Router.ConvertAsync(src, to, Ctx);
        Assert.Equal(Path.Combine(Dir, $"그림 (1).{to}"), Assert.Single(outs));
        if (to == "pdf")
        {
            using var pdf = PdfDocument.Open(outs[0]);
            Assert.Equal(1, pdf.NumberOfPages);
            var page = pdf.GetPage(1);
            Assert.Equal((64.0, 48.0), (page.Width, page.Height));
        }
        else
        {
            using var m = new MagickImage(outs[0]);
            Assert.Equal((64u, 48u), (m.Width, m.Height));
        }
        Assert.Equal(before, Sha256(src));
    }

    [Fact]
    public async Task Png_WithAlpha_ToJpg_HasWhiteBackground()
    {
        var src = Path.Combine(Dir, "투명.png");
        using (var img = new MagickImage(MagickColors.Transparent, 16, 16)) img.Write(src, MagickFormat.Png);
        var outs = await Router.ConvertAsync(src, "jpg", Ctx);
        using var m = new MagickImage(outs[0]);
        using var px = m.GetPixels();
        var c = px.GetPixel(0, 0).ToColor()!;
        const int max = 255;
        var scale = Quantum.Max / (double)max;
        Assert.InRange(c.R / scale, max - 3, max);
        Assert.InRange(c.G / scale, max - 3, max);
        Assert.InRange(c.B / scale, max - 3, max);
    }

    [Fact]
    public async Task CorruptJpg_ThrowsLimeException_AndLeavesNoFile()
    {
        var src = Path.Combine(Dir, "깨짐.jpg");
        File.WriteAllBytes(src, [1, 2, 3]);
        var ex = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, "png", Ctx));
        Assert.Equal("이미지를 읽을 수 없습니다.", ex.Message);
        Assert.False(File.Exists(Path.Combine(Dir, "깨짐.png")));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    public async Task ZeroByteImage_ThrowsLimeException_AndLeavesNoFile(string ext)
    {
        var src = Path.Combine(Dir, $"빈.{ext}");
        File.WriteAllBytes(src, []);
        var ex = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, ext == "png" ? "jpg" : "png", Ctx));
        Assert.Equal("이미지를 읽을 수 없습니다.", ex.Message);
        Assert.Equal([src], Directory.GetFiles(Dir));
    }

    [Fact]
    public async Task MislabeledPngAsJpg_StillConverts()
    {
        var png = FixtureFactory.Image(Dir, "원본.png");
        var src = Path.Combine(Dir, "가짜.jpg");
        File.Copy(png, src);
        var outs = await Router.ConvertAsync(src, "webp", Ctx);
        using var m = new MagickImage(outs[0]);
        Assert.Equal((64u, 48u), (m.Width, m.Height));
    }

    [Fact]
    public async Task ExifRotatedJpg_IsOrientedUpright()
    {
        var src = FixtureFactory.Image(Dir, "회전.jpg");
        // Splice a minimal EXIF APP1 segment (Orientation = 6, i.e. rotate 90 degrees) in after the SOI marker.
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 8,
            0x00, 0x01, 0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1, 0x00, 0x06, 0, 0,
            0, 0, 0, 0,
        ];
        var jpg = File.ReadAllBytes(src);
        File.WriteAllBytes(src, [.. jpg[..2], .. app1, .. jpg[2..]]);
        using (var chk = new MagickImage(src)) Assert.Equal(Orientation.RightTop, chk.Orientation);
        var outs = await Router.ConvertAsync(src, "png", Ctx);
        using var m = new MagickImage(outs[0]);
        Assert.Equal((48u, 64u), (m.Width, m.Height));
    }
}
