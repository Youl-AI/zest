namespace LimeWin.Core.Catalog;

public enum FormatCategory { Image, Gif, Audio, Video, Pdf, Document, Slides, Spreadsheet }

/// <summary>Optional engines/codecs available on this machine; unavailable ones drop their targets.</summary>
public sealed record Capabilities(bool Office, bool Ffmpeg, bool HeicWrite, bool AvifWrite, bool Ocr, bool BackgroundRemoval,
    IReadOnlySet<string>? UnavailableTargets = null)
{
    public static Capabilities All { get; } = new(true, true, true, true, true, true);
}

public static class FormatCatalog
{
    static readonly string[] Images = ["jpg", "png", "webp", "heic", "tiff", "avif", "bmp", "svg"];
    static readonly string[] ImageTargets = ["jpg", "png", "webp", "heic", "tiff", "avif", "bmp", "pdf"];
    static readonly string[] Gif = ["gif"];
    static readonly string[] Audio = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma"];
    static readonly string[] Video = ["mp4", "mov", "mkv", "webm", "avi", "wmv"];
    static readonly string[] Pdf = ["pdf"];
    static readonly string[] Documents = ["docx", "doc", "rtf", "odt", "txt", "md", "html"];
    static readonly string[] DocumentTargets = ["docx", "odt", "txt", "md", "html", "pdf"];
    static readonly string[] Slides = ["ppt", "pptx", "odp"];
    static readonly string[] SlideTargets = ["pdf", "jpg", "png"];
    static readonly string[] Spreadsheets = ["xlsx", "xls", "ods", "csv", "tsv"];
    static readonly string[] SpreadsheetTargets = ["xlsx", "ods", "csv", "tsv", "pdf"];
    // No docx: a hidden Word never finishes opening a PDF, and a visible one breaks spec §2 (Task 8).
    static readonly string[] PdfTargets = ["jpg", "png", "txt"];

    static readonly HashSet<string> InputOnly = ["svg", "webm", "doc", "rtf", "xls", "ppt", "odp"];

    static readonly Dictionary<string, string> Aliases = new()
    {
        ["jpeg"] = "jpg", ["jpe"] = "jpg", ["jfif"] = "jpg",
        ["tif"] = "tiff", ["heif"] = "heic",
        ["aif"] = "aiff", ["aifc"] = "aiff",
        ["markdown"] = "md", ["mdown"] = "md",
        ["htm"] = "html", ["xhtml"] = "html",
    };

    /// <summary>Every format the catalog accepts as a source (including input-only ones).</summary>
    public static IReadOnlyList<string> InputFormats { get; } =
        [.. Images, .. Gif, .. Audio, .. Video, .. Pdf, .. Documents, .. Slides, .. Spreadsheets];

    /// <summary>Normalized lowercase format of a file path, or null for no/empty extension or an existing directory.</summary>
    public static string? FormatOf(string path)
    {
        if (Directory.Exists(path)) return null;
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext) || ext.Length < 2) return null;
        var e = ext[1..].ToLowerInvariant();
        return Aliases.TryGetValue(e, out var canonical) ? canonical : e;
    }

    public static FormatCategory? CategoryOf(string format)
    {
        if (Images.Contains(format)) return FormatCategory.Image;
        if (Gif.Contains(format)) return FormatCategory.Gif;
        if (Audio.Contains(format)) return FormatCategory.Audio;
        if (Video.Contains(format)) return FormatCategory.Video;
        if (Pdf.Contains(format)) return FormatCategory.Pdf;
        if (Documents.Contains(format)) return FormatCategory.Document;
        if (Slides.Contains(format)) return FormatCategory.Slides;
        if (Spreadsheets.Contains(format)) return FormatCategory.Spreadsheet;
        return null;
    }

    public static IReadOnlyList<string> TargetsFor(string format, Capabilities caps)
    {
        var category = CategoryOf(format);
        if (category is null) return [];

        IEnumerable<string> candidates = category.Value switch
        {
            FormatCategory.Image => ImageTargets,
            FormatCategory.Gif => Video,
            FormatCategory.Audio => Audio,
            FormatCategory.Video => Video.Concat(["gif", "mp3"]),
            FormatCategory.Pdf => PdfTargets,
            FormatCategory.Document => DocumentTargets,
            FormatCategory.Slides => format == "ppt" ? SlideTargets.Append("pptx") : SlideTargets,
            FormatCategory.Spreadsheet => SpreadsheetTargets,
            _ => [],
        };

        return candidates
            .Where(t => t != format && !InputOnly.Contains(t) && IsAvailable(format, category.Value, t, caps))
            .ToList();
    }

    static bool IsAvailable(string source, FormatCategory category, string target, Capabilities caps)
    {
        if (target == "heic" && !caps.HeicWrite) return false;
        if (target == "avif" && !caps.AvifWrite) return false;
        if (caps.UnavailableTargets?.Contains(target) == true) return false;

        // Ffmpeg: Gif, Audio, Video sources entirely.
        if (category is FormatCategory.Gif or FormatCategory.Audio or FormatCategory.Video && !caps.Ffmpeg) return false;

        // Office: all Slides, all Spreadsheets, all Documents except md<->html.
        if (!caps.Office)
        {
            switch (category)
            {
                case FormatCategory.Slides:
                case FormatCategory.Spreadsheet: return false;
                case FormatCategory.Document:
                    return (source, target) is ("md", "html") or ("html", "md");
            }
        }
        return true;
    }
}
