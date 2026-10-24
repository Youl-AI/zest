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
    public async Task SameNameExists_GetsNumbered()
    {
        var src = FixtureFactory.JpegWithTags(Dir, "photo.jpg", CameraTags);
        File.WriteAllText(Path.Combine(Dir, "photo (edited).jpg"), "taken");

        var output = await MetadataWriter.ApplyAsync(src, Edit(changed: new() { ["exif:Artist"] = "홍길동" }), Ctx);

        Assert.Equal(Path.Combine(Dir, "photo (edited) 2.jpg"), output);
        Assert.Equal("taken", File.ReadAllText(Path.Combine(Dir, "photo (edited).jpg")));
    }

    static string PixelHash(string path)
    {
        using var image = new MagickImage(path);
        using var pixels = image.GetPixels();
        return System.Convert.ToHexString(SHA256.HashData(pixels.ToByteArray(PixelMapping.RGB)!));
    }
}
