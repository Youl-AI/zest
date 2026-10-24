using Zest.Core.Metadata;

namespace Zest.Core.Tests;

public class MetadataEditBuilderTests
{
    static readonly MetadataTag[] Photo =
    [
        new("file:format", "파일", "형식", "JPG", Editable: false, Removable: false),
        new("exif:Make", "카메라", "제조사", "Apple", Editable: true, Removable: true),
        new("exif:Artist", "설명", "작성자", "", Editable: true, Removable: true),
        new("exif:FNumber", "기타", "FNumber", "1.8", Editable: false, Removable: true),
        new("exif:GPSLatitude", "위치", "위도", "33.4996° N", Editable: false, Removable: true),
        new("media:location", "미디어", "위치", "+33.4996+126.5312/", Editable: true, Removable: true),
    ];

    static Dictionary<string, string> Current(params (string Id, string Text)[] edits)
    {
        var current = Photo.Where(t => t.Editable).ToDictionary(t => t.Id, t => t.Value);
        foreach (var (id, text) in edits) current[id] = text;
        return current;
    }

    static MetadataEdit Build(Dictionary<string, string> current, string[]? removed = null, bool location = false, bool all = false) =>
        MetadataEditBuilder.Build(Photo, current, new HashSet<string>(removed ?? []), location, all);

    [Fact]
    public void Untouched_IsEmpty()
    {
        var edit = Build(Current());
        Assert.Empty(edit.Changed);
        Assert.Empty(edit.Removed);
        Assert.False(edit.RemoveLocation);
        Assert.False(edit.RemoveAll);
    }

    [Fact]
    public void ChangedText_IsTrimmed()
    {
        var edit = Build(Current(("exif:Artist", "  김하늘 ")));
        Assert.Equal("김하늘", edit.Changed["exif:Artist"]);
        Assert.Single(edit.Changed);
        Assert.Empty(edit.Removed);
    }

    [Fact]
    public void WhitespaceOnlyDifference_IsNoChange() => Assert.Empty(Build(Current(("exif:Make", " Apple  "))).Changed);

    [Fact]
    public void EmptiedField_IsRemovedNotChanged()
    {
        var edit = Build(Current(("exif:Make", "   ")));
        Assert.Empty(edit.Changed);
        Assert.Equal(["exif:Make"], edit.Removed);
    }

    [Fact]
    public void StruckRow_IsRemovedEvenIfEdited()
    {
        var edit = Build(Current(("exif:Make", "Canon")), ["exif:Make", "exif:FNumber"]);
        Assert.Empty(edit.Changed);
        Assert.Equal(new HashSet<string> { "exif:Make", "exif:FNumber" }, edit.Removed);
    }

    [Fact]
    public void ReadOnlyRow_IgnoresAnyText()
    {
        var current = Current();
        current["exif:FNumber"] = "2.8";
        current["file:format"] = "PNG";
        Assert.Empty(Build(current).Changed);
    }

    [Fact]
    public void UnknownOrNotRemovableId_IsIgnored() => Assert.Empty(Build(Current(), ["exif:Nope", "file:format"]).Removed);

    [Fact]
    public void RemoveLocation_SkipsLocationRows()
    {
        var edit = Build(Current(("media:location", "+37.5+127.0/")), ["exif:GPSLatitude"], location: true);
        Assert.True(edit.RemoveLocation);
        Assert.Empty(edit.Changed);
        Assert.Empty(edit.Removed);
    }

    [Fact]
    public void RemoveAll_DropsEverythingElse()
    {
        var edit = Build(Current(("exif:Artist", "김하늘")), ["exif:Make"], location: true, all: true);
        Assert.True(edit.RemoveAll);
        Assert.Empty(edit.Changed);
        Assert.Empty(edit.Removed);
    }

    [Theory]
    [InlineData("exif:GPSLatitude", true)]
    [InlineData("media:com.apple.quicktime.location.ISO6709", true)]
    [InlineData("exif:Model", false)]
    [InlineData("xmp:packet", false)]
    [InlineData("nofamily", false)]
    public void IsLocation_ById(string id, bool expected) => Assert.Equal(expected, MetadataEditBuilder.IsLocation(id));
}
