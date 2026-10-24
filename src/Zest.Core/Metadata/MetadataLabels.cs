using Zest.Core.Catalog;

namespace Zest.Core.Metadata;

/// <summary>Korean group and label for a tag key; unknown keys show as-is under 기타.</summary>
public static class MetadataLabels
{
    const string Other = "기타";

    static readonly Dictionary<string, (string Group, string Label)> Exif = new()
    {
        ["Make"] = ("카메라", "제조사"),
        ["Model"] = ("카메라", "모델"),
        ["LensMake"] = ("카메라", "렌즈 제조사"),
        ["LensModel"] = ("카메라", "렌즈"),
        ["DateTimeOriginal"] = ("촬영", "촬영 일시"),
        ["DateTimeDigitized"] = ("촬영", "디지털화 일시"),
        ["DateTime"] = ("촬영", "수정 일시"),
        ["Software"] = ("설명", "소프트웨어"),
        ["Artist"] = ("설명", "작성자"),
        ["Copyright"] = ("설명", "저작권"),
        ["ImageDescription"] = ("설명", "설명"),
        ["Orientation"] = (Other, "방향"),
        ["GPSLatitude"] = ("위치", "위도"),
        ["GPSLongitude"] = ("위치", "경도"),
        ["GPSAltitude"] = ("위치", "고도"),
    };

    static readonly Dictionary<string, string> Pdf = new()
    {
        ["Title"] = "제목", ["Author"] = "작성자", ["Subject"] = "주제", ["Keywords"] = "키워드",
        ["Creator"] = "만든 프로그램", ["Producer"] = "변환 프로그램",
        ["CreationDate"] = "만든 날짜", ["ModDate"] = "수정한 날짜",
    };

    static readonly Dictionary<string, string> Media = new()
    {
        ["title"] = "제목", ["artist"] = "아티스트", ["album"] = "앨범", ["album_artist"] = "앨범 아티스트",
        ["genre"] = "장르", ["date"] = "연도", ["track"] = "트랙", ["composer"] = "작곡가",
        ["comment"] = "설명", ["encoder"] = "인코더", ["location"] = "위치",
    };

    public static (string Group, string Label) For(string family, string key)
    {
        switch (family)
        {
            case "exif":
                if (Exif.TryGetValue(key, out var known)) return known;
                if (key.StartsWith("GPS", StringComparison.Ordinal)) return ("위치", key);
                break;
            case "pdf":
                if (Pdf.TryGetValue(key, out var pdf)) return ("문서", pdf);
                break;
            case "media":
                if (Media.TryGetValue(key, out var media)) return ("미디어", media);
                break;
        }
        return (Other, key);
    }

    /// <summary>True for EXIF GPS tags and, in any family, a key containing "location" (QuickTime, ©xyz-style location keys).</summary>
    public static bool IsLocationKey(string family, string key) =>
        (family == "exif" && key.StartsWith("GPS", StringComparison.Ordinal))
        || key.Contains("location", StringComparison.OrdinalIgnoreCase);
}

public static class MetadataFormats
{
    /// <summary>Formats whose metadata can be read and rewritten here; media needs both ffmpeg and ffprobe.</summary>
    public static bool Supports(string format, Capabilities caps)
    {
        switch (format)
        {
            case "jpg" or "png" or "webp" or "tiff" or "pdf": return true;
            case "avif": return caps.AvifWrite;
            case "heic": return caps.HeicWrite;
        }
        return FormatCatalog.CategoryOf(format) is FormatCategory.Audio or FormatCategory.Video && caps.Ffmpeg && caps.Ffprobe;
    }
}
