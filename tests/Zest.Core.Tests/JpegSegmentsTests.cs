using System.Text;
using ImageMagick;
using Zest.Core.Metadata;
using Zest.Core.Tests.Fixtures;

namespace Zest.Core.Tests;

public class JpegSegmentsTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static readonly Dictionary<ExifTag<string>, string> CameraTags = new()
    {
        [ExifTag.Make] = "Google",
        [ExifTag.Model] = "Pixel 9",
    };

    [Fact]
    public void Rewrite_KeepsScanDataByteForByte()
    {
        var src = File.ReadAllBytes(FixtureFactory.JpegWithTags(Dir, "a.jpg", CameraTags, withGps: true, withXmpGps: true, withIcc: true));
        var payload = new ExifProfile();
        payload.SetValue(ExifTag.Artist, "홍길동");

        var output = JpegSegments.Rewrite(src, payload.ToByteArray(), dropXmp: true, dropIptc: true);

        Assert.Equal(src[ScanStart(src)..], output[ScanStart(output)..]);
        Assert.Equal("홍길동", new ExifProfile(ExifPayload(output)!).GetValue(ExifTag.Artist)?.Value);
    }

    [Fact]
    public void Rewrite_DropsXmp_KeepsIcc()
    {
        var src = File.ReadAllBytes(FixtureFactory.JpegWithTags(Dir, "a.jpg", CameraTags, withXmpGps: true, withIcc: true));
        Assert.True(Contains(src, "http://ns.adobe.com/xap/1.0/\0"));

        var output = JpegSegments.Rewrite(src, null, dropXmp: true, dropIptc: false);

        Assert.False(Contains(output, "http://ns.adobe.com/xap/1.0/\0"));
        Assert.True(Contains(output, "ICC_PROFILE\0"));
        Assert.Null(ExifPayload(output));
        using var m = new MagickImage(output);
        Assert.NotNull(m.GetColorProfile());
    }

    [Fact]
    public void Rewrite_Malformed_Throws()
    {
        var ex = Assert.Throws<ZestException>(() => JpegSegments.Rewrite([0xFF, 0xD8, 0x00], null, false, false));
        Assert.Equal("메타데이터를 읽을 수 없습니다.", ex.Message);
    }

    /// <summary>Offset of the SOS marker (start of the entropy-coded data).</summary>
    static int ScanStart(byte[] jpeg)
    {
        var pos = 2;
        while (jpeg[pos + 1] != 0xDA) pos += 2 + (jpeg[pos + 2] << 8 | jpeg[pos + 3]);
        return pos;
    }

    /// <summary>The APP1 Exif payload (with its `Exif\0\0` prefix), or null when there is none.</summary>
    static byte[]? ExifPayload(byte[] jpeg)
    {
        var pos = 2;
        while (jpeg[pos + 1] != 0xDA)
        {
            var length = jpeg[pos + 2] << 8 | jpeg[pos + 3];
            if (jpeg[pos + 1] == 0xE1 && Encoding.ASCII.GetString(jpeg, pos + 4, 6) == "Exif\0\0") return jpeg[(pos + 4)..(pos + 2 + length)];
            pos += 2 + length;
        }
        return null;
    }

    static bool Contains(byte[] data, string ascii) => data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(ascii)) >= 0;
}
