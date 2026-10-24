using System.Security.Cryptography;
using ImageMagick;
using Zest.Core.Catalog;
using Zest.Core.Jobs;
using Zest.Core.Metadata;
using Zest.Core.Tests.Fixtures;

namespace Zest.Core.Tests;

public class ImageMetadataTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static readonly Dictionary<ExifTag<string>, string> CameraTags = new()
    {
        [ExifTag.Make] = "Google",
        [ExifTag.Model] = "Pixel 9",
    };

    static Task<IReadOnlyList<MetadataTag>> Read(string path) => MetadataReader.ReadAsync(path, Capabilities.All, CancellationToken.None);

    static MetadataEdit Edit(Dictionary<string, string>? changed = null, HashSet<string>? removed = null) =>
        new(changed ?? [], removed ?? [], RemoveLocation: false, RemoveAll: false);

    [Fact]
    public async Task Read_Jpeg_ListsCameraAndLocation()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags, withGps: true);

        var tags = await Read(src);

        var model = Assert.Single(tags, t => t.Id == "exif:Model");
        Assert.Equal(("카메라", "모델", "Pixel 9", true, true), (model.Group, model.Label, model.Value, model.Editable, model.Removable));
        var gps = tags.Where(t => t.Id.StartsWith("exif:GPS", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(gps);
        Assert.All(gps, t => Assert.Equal(("위치", false, true), (t.Group, t.Editable, t.Removable)));
        Assert.Equal("33.5000° N", Assert.Single(tags, t => t.Id == "exif:GPSLatitude").Value);
        Assert.Equal("126.5250° E", Assert.Single(tags, t => t.Id == "exif:GPSLongitude").Value);
        var size = Assert.Single(tags, t => t.Id == "file:size");
        Assert.Equal(("파일", "크기", false, false), (size.Group, size.Label, size.Editable, size.Removable));
        Assert.StartsWith("64 × 48 · ", size.Value);
        Assert.Equal("JPG", Assert.Single(tags, t => t.Id == "file:format").Value);
    }

    [Fact]
    public async Task Edit_KoreanText_RoundTrips()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags);
        var before = File.ReadAllBytes(src);

        var output = await MetadataWriter.ApplyAsync(src, Edit(changed: new() { ["exif:Artist"] = "홍길동" }), Ctx);

        Assert.EndsWith(" (edited).jpg", output);
        Assert.Equal("홍길동", Assert.Single(await Read(output), t => t.Id == "exif:Artist").Value);
        Assert.Equal(before, File.ReadAllBytes(src));
    }

    [Fact]
    public async Task Remove_OneTag()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags);

        var output = await MetadataWriter.ApplyAsync(src, Edit(removed: ["exif:Model"]), Ctx);

        var tags = await Read(output);
        Assert.DoesNotContain(tags, t => t.Id == "exif:Model");
        Assert.Equal("Google", Assert.Single(tags, t => t.Id == "exif:Make").Value);
    }

    [Fact]
    public async Task RemoveLocation_AlsoDropsXmp()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags, withGps: true, withXmpGps: true);
        var xmp = Assert.Single(await Read(src), t => t.Id == "xmp:packet");
        Assert.Equal(("기타", "XMP 데이터 있음", false, true), (xmp.Group, xmp.Label, xmp.Editable, xmp.Removable));

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.LocationOnly, Ctx);

        var tags = await Read(output);
        Assert.DoesNotContain(tags, t => t.Id.StartsWith("exif:GPS", StringComparison.Ordinal));
        Assert.DoesNotContain(tags, t => t.Id.StartsWith("xmp:", StringComparison.Ordinal));
        Assert.Equal("Pixel 9", Assert.Single(tags, t => t.Id == "exif:Model").Value);
        Assert.True(File.ReadAllBytes(output).AsSpan().IndexOf("GPSLatitude"u8) < 0);
    }

    [Fact]
    public async Task RemoveAll_KeepsOrientationAndIcc()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags, withGps: true, withXmpGps: true, orientation: 6, withIcc: true);

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.Everything, Ctx);

        Assert.EndsWith(" (no metadata).jpg", output);
        using var m = new MagickImage();
        m.Ping(output);
        var exif = m.GetExifProfile();
        Assert.NotNull(exif);
        var only = Assert.Single(exif.Values);
        Assert.Equal(ExifTag.Orientation, only.Tag);
        Assert.Equal((ushort)6, only.GetValue());
        Assert.NotNull(m.GetColorProfile());
        Assert.Null(m.GetXmpProfile());
        var tags = await Read(output);
        Assert.DoesNotContain(tags, t => t.Id is "exif:Make" or "exif:Model");
    }

    [Theory]
    [InlineData((ushort)0)] // undefined, as ImageMagick writes it when the image has no orientation
    [InlineData((ushort)1)]
    public void RemoveAll_UprightOrUndefinedOrientation_LeavesNoExif(ushort orientation)
    {
        var source = new ExifProfile();
        source.SetValue(ExifTag.Orientation, orientation);
        source.SetValue(ExifTag.Make, "Google");

        Assert.Null(ImageMetadata.BuildExif(source, MetadataEdit.Everything, out var changed));
        Assert.True(changed);
    }

    [Fact]
    public async Task Read_ShowsRationalsAsText()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags);
        using (var image = new MagickImage(src))
        {
            var exif = image.GetExifProfile()!;
            exif.SetValue(ExifTag.ExposureTime, new Rational(1, 120));
            exif.SetValue(ExifTag.FNumber, new Rational(18, 10));
            exif.SetValue(ExifTag.ISOSpeedRatings, [(ushort)100]);
            image.SetProfile(exif);
            image.Write(src, MagickFormat.Jpeg);
        }

        var tags = await Read(src);

        Assert.Equal("1/120", Assert.Single(tags, t => t.Id == "exif:ExposureTime").Value);
        Assert.Equal("1.8", Assert.Single(tags, t => t.Id == "exif:FNumber").Value);
        Assert.Equal("100", Assert.Single(tags, t => t.Id == "exif:ISOSpeedRatings").Value);
        Assert.False(Assert.Single(tags, t => t.Id == "exif:FNumber").Editable);
    }

    [Fact]
    public async Task Jpeg_PixelsUnchanged()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags, withGps: true, withXmpGps: true, orientation: 6, withIcc: true);
        var edit = new MetadataEdit(new Dictionary<string, string> { ["exif:Artist"] = "홍길동" }, new HashSet<string> { "exif:Make" },
            RemoveLocation: true, RemoveAll: false);

        var output = await MetadataWriter.ApplyAsync(src, edit, Ctx);

        Assert.Equal(PixelHash(src), PixelHash(output));
    }

    [Fact]
    public async Task Png_Edit_RoundTrips()
    {
        var src = FixtureFactory.Image(Dir, "photo.png");
        using (var image = new MagickImage(src))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Artist, "Someone");
            image.SetProfile(exif);
            image.Write(src, MagickFormat.Png);
        }

        var output = await MetadataWriter.ApplyAsync(src, Edit(changed: new() { ["exif:Artist"] = "김" }), Ctx);

        Assert.EndsWith(" (edited).png", output);
        Assert.Equal("김", Assert.Single(await Read(output), t => t.Id == "exif:Artist").Value);
    }

    [Fact]
    public async Task Webp_RemoveAll_KeepsOrientationAndIcc()
    {
        var src = FixtureFactory.Image(Dir, "photo.webp");
        using (var image = new MagickImage(src))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            exif.SetValue(ExifTag.Model, "Pixel 9");
            image.Orientation = Orientation.RightTop;
            image.SetProfile(exif);
            image.SetProfile(ColorProfiles.SRGB);
            image.Write(src, MagickFormat.WebP);
        }

        Assert.Equal("Pixel 9", Assert.Single(await Read(src), t => t.Id == "exif:Model").Value); // Ping alone shows no WebP profiles

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.Everything, Ctx);

        Assert.EndsWith(" (no metadata).webp", output);
        using var m = new MagickImage(output);
        var only = Assert.Single(m.GetExifProfile()!.Values);
        Assert.Equal(((ushort)6), only.GetValue());
        Assert.NotNull(m.GetColorProfile());
        Assert.Equal("6", Assert.Single(await Read(output), t => t.Id.StartsWith("exif:", StringComparison.Ordinal)).Value);
    }

    [Fact]
    public async Task Tiff_IsRefused_LeavesNothing()
    {
        var src = FixtureFactory.Image(Dir, "photo.tiff");

        var read = await Assert.ThrowsAsync<ZestException>(() => Read(src));
        var write = await Assert.ThrowsAsync<ZestException>(() => MetadataWriter.ApplyAsync(src, MetadataEdit.Everything, Ctx));

        Assert.Equal("이 형식은 메타데이터를 바꿀 수 없습니다.", read.Message);
        Assert.Equal("이 형식은 메타데이터를 바꿀 수 없습니다.", write.Message);
        Assert.Equal([src], Directory.GetFiles(Dir));
    }

    [Fact]
    public async Task SameNameExists_GetsNumbered()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags);
        File.WriteAllText(Path.Combine(Dir, "photo (edited).jpg"), "taken");

        var output = await MetadataWriter.ApplyAsync(src, Edit(changed: new() { ["exif:Artist"] = "홍길동" }), Ctx);

        Assert.Equal(Path.Combine(Dir, "photo (edited) 2.jpg"), output);
        Assert.Equal("taken", File.ReadAllText(Path.Combine(Dir, "photo (edited).jpg")));
    }

    static readonly byte[] Adobe = [.. "Adobe"u8, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x01]; // APP14, transform 1 = YCbCr

    [Fact]
    public async Task Jpeg_RemoveAll_DropsCommentsAndVendorSegments_KeepsDecodingOnes()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags, withGps: true, orientation: 6, withIcc: true);
        FixtureFactory.InsertJpegSegments(src,
            FixtureFactory.JpegSegment(0xFE, "secret comment"),
            FixtureFactory.JpegSegment(0xEC, "Ducky vendor data"),
            FixtureFactory.JpegSegment(0xEB, "JP c2pa manifest with GPS"),
            FixtureFactory.JpegSegment(0xEE, Adobe));

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.Everything, Ctx);

        var bytes = File.ReadAllBytes(output);
        Assert.False(Has(bytes, "secret comment"));
        Assert.False(Has(bytes, "Ducky vendor data"));
        Assert.False(Has(bytes, "c2pa manifest"));
        var markers = Segments(bytes).Select(s => s.Marker).ToList();
        Assert.Contains((byte)0xE0, markers); // JFIF
        Assert.Contains(Segments(bytes), s => s.Marker == 0xE2 && s.Body.AsSpan().StartsWith("ICC_PROFILE\0"u8));
        Assert.Contains(Segments(bytes), s => s.Marker == 0xEE && s.Body.SequenceEqual(Adobe));
        Assert.Equal(PixelHash(src), PixelHash(output));
    }

    [Fact]
    public async Task Jpeg_RemoveLocation_DropsMpfAndTrailer()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags, withGps: true);
        FixtureFactory.InsertJpegSegments(src, FixtureFactory.JpegSegment(0xE2, [.. "MPF\0"u8, .. "MM\0*\0\0\0\b"u8]));
        var primary = File.ReadAllBytes(src);
        File.WriteAllBytes(src, [.. primary, 0xFF, 0xD8, .. " gain map GPSLatitude 33.5 "u8, 0xFF, 0xD9]);

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.LocationOnly, Ctx);

        var bytes = File.ReadAllBytes(output);
        Assert.DoesNotContain(Segments(bytes), s => s.Marker == 0xE2 && s.Body.AsSpan().StartsWith("MPF\0"u8));
        Assert.False(Has(bytes, "GPSLatitude"));
        Assert.False(Has(bytes, "gain map"));
        Assert.Equal([0xFF, 0xD9], bytes[^2..]);
        Assert.Equal(PixelHash(src), PixelHash(output));
    }

    [Fact]
    public async Task Jpeg_VariantExifHeader_RemoveLocation_LeavesOneExifWithoutGps()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags, withGps: true);
        var bytes = File.ReadAllBytes(src);
        var exif = Segments(bytes).Single(s => s.Marker == 0xE1 && s.Body.AsSpan().StartsWith("Exif\0\0"u8));
        bytes[exif.Offset + 4 + 5] = 0xFF; // header "Exif\0\xFF": ImageMagick still takes it as the EXIF profile
        File.WriteAllBytes(src, bytes);
        var gpsBefore = GpsByteRuns(bytes);
        Assert.NotEmpty(gpsBefore);
        Assert.Contains(await Read(src), t => t.Id == "exif:GPSLatitude"); // the location banner must see it

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.LocationOnly, Ctx);

        var outBytes = File.ReadAllBytes(output);
        var exifs = Segments(outBytes).Where(s => s.Marker == 0xE1 && System.Text.Ascii.EqualsIgnoreCase(s.Body.AsSpan(0, 4), "exif"u8)).ToList();
        var only = Assert.Single(exifs);
        Assert.DoesNotContain(new ExifProfile(only.Body).Values, v => v.Tag.ToString()!.StartsWith("GPS", StringComparison.Ordinal));
        Assert.All(gpsBefore, run => Assert.True(outBytes.AsSpan().IndexOf(run) < 0));
        Assert.Equal("Pixel 9", Assert.Single(await Read(output), t => t.Id == "exif:Model").Value);
    }

    [Fact]
    public async Task Jpeg_PlainEdit_KeepsCommentAndVendorSegments()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags);
        byte[] com = FixtureFactory.JpegSegment(0xFE, "secret comment"), app12 = FixtureFactory.JpegSegment(0xEC, "Ducky vendor data");
        FixtureFactory.InsertJpegSegments(src, com, app12);

        var output = await MetadataWriter.ApplyAsync(src, Edit(changed: new() { ["exif:Artist"] = "홍길동" }), Ctx);

        var bytes = File.ReadAllBytes(output);
        Assert.True(bytes.AsSpan().IndexOf(com) >= 0);
        Assert.True(bytes.AsSpan().IndexOf(app12) >= 0);
        Assert.Equal("홍길동", Assert.Single(await Read(output), t => t.Id == "exif:Artist").Value);
    }

    [Fact]
    public async Task Jpeg_RemoveExifTag_AlsoDropsXmpCopy()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", new Dictionary<ExifTag<string>, string>
        {
            [ExifTag.Make] = "Google",
            [ExifTag.Artist] = "홍길동",
        });
        var xmp = """
            <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:creator><rdf:Seq><rdf:li>홍길동</rdf:li></rdf:Seq></dc:creator></rdf:Description>
            </rdf:RDF></x:xmpmeta>
            <?xpacket end="w"?>
            """;
        FixtureFactory.InsertJpegSegments(src,
            FixtureFactory.JpegSegment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. System.Text.Encoding.UTF8.GetBytes(xmp)]));
        Assert.Contains(await Read(src), t => t.Id == "xmp:packet");

        var output = await MetadataWriter.ApplyAsync(src, Edit(removed: ["exif:Artist"]), Ctx);

        var bytes = File.ReadAllBytes(output);
        Assert.True(bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("홍길동")) < 0);
        Assert.True(bytes.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("홍길동")) < 0);
        Assert.True(bytes.AsSpan().IndexOf(System.Text.Encoding.BigEndianUnicode.GetBytes("홍길동")) < 0);
        Assert.Equal("Google", Assert.Single(await Read(output), t => t.Id == "exif:Make").Value);
    }

    [Fact]
    public async Task Jpeg_ExifEdit_DropsMpfAndTrailer()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags);
        FixtureFactory.InsertJpegSegments(src, FixtureFactory.JpegSegment(0xE2, [.. "MPF\0"u8, .. "MM\0*\0\0\0\b"u8]));
        var primary = File.ReadAllBytes(src);
        File.WriteAllBytes(src, [.. primary, 0xFF, 0xD8, .. " second image Artist old "u8, 0xFF, 0xD9]);

        var output = await MetadataWriter.ApplyAsync(src, Edit(changed: new() { ["exif:Artist"] = "홍길동" }), Ctx);

        var bytes = File.ReadAllBytes(output);
        Assert.DoesNotContain(Segments(bytes), s => s.Marker == 0xE2 && s.Body.AsSpan().StartsWith("MPF\0"u8));
        Assert.False(Has(bytes, "second image"));
        Assert.Equal(primary[SosOffset(primary)..], bytes[SosOffset(bytes)..]); // the scans, ending at the primary EOI
        Assert.Equal(PixelHash(src), PixelHash(output));
        Assert.Equal("홍길동", Assert.Single(await Read(output), t => t.Id == "exif:Artist").Value);
    }

    /// <summary>The marker segments before the first SOS, with their offsets.</summary>
    static List<(byte Marker, int Offset, byte[] Body)> Segments(byte[] jpeg)
    {
        var list = new List<(byte, int, byte[])>();
        var pos = 2;
        while (jpeg[pos + 1] != 0xDA)
        {
            var length = jpeg[pos + 2] << 8 | jpeg[pos + 3];
            list.Add((jpeg[pos + 1], pos, jpeg[(pos + 4)..(pos + 2 + length)]));
            pos += 2 + length;
        }
        return list;
    }

    static int SosOffset(byte[] jpeg)
    {
        var last = Segments(jpeg)[^1];
        return last.Offset + 4 + last.Body.Length;
    }

    /// <summary>The fixture's GPS latitude seconds as stored (5988/100 or 1497/25), in either byte order, as found in the file.</summary>
    static List<byte[]> GpsByteRuns(byte[] jpeg)
    {
        static byte[] Pair(uint a, uint b, bool little)
        {
            var x = BitConverter.GetBytes(a);
            var y = BitConverter.GetBytes(b);
            if (!little) { Array.Reverse(x); Array.Reverse(y); }
            return [.. x, .. y];
        }
        return new[] { Pair(5988, 100, true), Pair(5988, 100, false), Pair(1497, 25, true), Pair(1497, 25, false) }
            .Where(run => jpeg.AsSpan().IndexOf(run) >= 0).ToList();
    }

    static bool Has(byte[] data, string ascii) => data.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(ascii)) >= 0;

    static string PixelHash(string path)
    {
        using var image = new MagickImage(path);
        using var pixels = image.GetPixels();
        return System.Convert.ToHexString(SHA256.HashData(pixels.ToByteArray(PixelMapping.RGB)!));
    }
}
