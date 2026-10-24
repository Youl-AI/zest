using Zest.Core.Catalog;
using Zest.Core.Metadata;

namespace Zest.Core.Tests;

public class MetadataLabelsTests
{
    [Fact] public void Exif_Model_IsCamera() => Assert.Equal(("카메라", "모델"), MetadataLabels.For("exif", "Model"));
    [Fact] public void Media_AlbumArtist_IsMedia() => Assert.Equal(("미디어", "앨범 아티스트"), MetadataLabels.For("media", "album_artist"));
    [Fact] public void Exif_Unknown_ShowsKeyUnderOther() => Assert.Equal(("기타", "WhiteBalance"), MetadataLabels.For("exif", "WhiteBalance"));
    [Fact] public void Exif_Orientation_IsOther() => Assert.Equal(("기타", "방향"), MetadataLabels.For("exif", "Orientation"));
    [Fact] public void Exif_Gps_IsLocation() => Assert.Equal(("위치", "위도"), MetadataLabels.For("exif", "GPSLatitude"));
    [Fact] public void Pdf_Title_IsDocument() => Assert.Equal(("문서", "제목"), MetadataLabels.For("pdf", "Title"));

    [Fact] public void IsLocationKey_QuickTimeLocation() => Assert.True(MetadataLabels.IsLocationKey("media", "com.apple.quicktime.location.ISO6709"));
    [Fact] public void IsLocationKey_ExifGps() => Assert.True(MetadataLabels.IsLocationKey("exif", "GPSLatitude"));
    [Fact] public void IsLocationKey_ExifModel() => Assert.False(MetadataLabels.IsLocationKey("exif", "Model"));

    [Fact]
    public void Edit_LocationOnly_RemovesOnlyLocation()
    {
        var e = MetadataEdit.LocationOnly;
        Assert.Empty(e.Changed);
        Assert.Empty(e.Removed);
        Assert.True(e.RemoveLocation);
        Assert.False(e.RemoveAll);
    }

    [Fact] public void Edit_Everything_RemovesAll() => Assert.True(MetadataEdit.Everything.RemoveAll);

    [Fact]
    public void Formats_Rules()
    {
        var all = Capabilities.All;
        Assert.True(MetadataFormats.Supports("jpg", all));
        Assert.True(MetadataFormats.Supports("pdf", all));
        Assert.False(MetadataFormats.Supports("bmp", all));
        Assert.False(MetadataFormats.Supports("gif", all));
        Assert.False(MetadataFormats.Supports("avif", all with { AvifWrite = false }));
        Assert.True(MetadataFormats.Supports("mp4", all));
        Assert.False(MetadataFormats.Supports("mp3", all with { Ffprobe = false }));
    }
}
