using System.Globalization;
using System.Reflection;
using ImageMagick;
using Zest.Core.Catalog;
using Zest.Core.Convert;
using Zest.Core.Jobs;
using Zest.Core.Output;
using Zest.Core.Tools;

namespace Zest.Core.Metadata;

/// <summary>
/// EXIF, XMP and IPTC of images. Reading never decodes pixels; writing copies JPEG scan data byte for byte
/// (<see cref="JpegSegments"/>) and re-encodes other formats through Magick (lossless ones as they are, lossy at quality 97).
/// </summary>
internal static class ImageMetadata
{
    public const string XmpId = "xmp:packet", IptcId = "iptc:data";
    const string Unreadable = "메타데이터를 읽을 수 없습니다.";
    const string NotEditable = "이 형식은 메타데이터를 바꿀 수 없습니다.";

    static readonly HashSet<string> Writable = ["jpg", "png", "webp", "avif", "heic"]; // no tiff: see MetadataFormats.Supports
    /// <summary>Formats whose profiles Magick reports on a ping; WebP (and HEIC, unverified here) only show them on a full read.</summary>
    static readonly HashSet<string> PingShowsProfiles = ["jpg", "png", "avif"];
    static readonly string[] GroupOrder = ["파일", "카메라", "촬영", "위치", "설명", "기타"];

    /// <summary>Every EXIF text tag Magick knows, by the name used in ids ("Artist", "ImageDescription"...).</summary>
    static readonly Dictionary<string, ExifTag<string>> TextTags = typeof(ExifTag)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.PropertyType == typeof(ExifTag<string>))
        .Select(p => (ExifTag<string>)p.GetValue(null)!)
        .DistinctBy(t => t.ToString())
        .ToDictionary(t => t.ToString()!, StringComparer.Ordinal);

    // ---- Read ----

    public static IReadOnlyList<MetadataTag> Read(string path, string format, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var image = new MagickImage();
            if (PingShowsProfiles.Contains(format)) image.Ping(bytes); // no pixel decode
            else image.Read(bytes, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
            token.ThrowIfCancellationRequested();

            var rows = new List<MetadataTag>
            {
                new("file:format", "파일", "형식", format.ToUpperInvariant(), false, false),
                new("file:size", "파일", "크기", $"{image.Width} × {image.Height} · {SizeText.Format(bytes.LongLength)}", false, false),
            };
            if (ExifOf(image, out _) is { } exif)
            {
                foreach (var value in exif.Values)
                {
                    var key = value.Tag.ToString()!;
                    var (group, label) = MetadataLabels.For("exif", key);
                    var location = MetadataLabels.IsLocationKey("exif", key);
                    var editable = value.DataType == ExifDataType.String && !location;
                    rows.Add(new($"exif:{key}", group, label, Describe(value, exif), editable, Removable: true));
                }
            }
            if (image.GetXmpProfile() is { } xmp) rows.Add(PresenceRow(XmpId, "XMP 데이터 있음", xmp));
            if (image.GetIptcProfile() is { } iptc) rows.Add(PresenceRow(IptcId, "IPTC 데이터 있음", iptc));
            return [.. rows.OrderBy(r => Array.IndexOf(GroupOrder, r.Group) is var i and >= 0 ? i : GroupOrder.Length)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or MagickException or ArgumentException
                                      or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new ZestException(Unreadable);
        }
    }

    static MetadataTag PresenceRow(string id, string label, IImageProfile profile) =>
        new(id, "기타", label, SizeText.Format(profile.ToByteArray()?.LongLength ?? 0), Editable: false, Removable: true);

    /// <summary>Readable text for one EXIF value: GPS coordinates as decimal degrees, short fractions as "1/120", other rationals as decimals.</summary>
    internal static string Describe(IExifValue value, IExifProfile profile)
    {
        var key = value.Tag.ToString()!;
        var raw = value.GetValue();
        if (key is "GPSLatitude" or "GPSLongitude" or "GPSDestLatitude" or "GPSDestLongitude" && raw is Rational[] { Length: 3 } dms)
        {
            var degrees = dms[0].ToDouble() + dms[1].ToDouble() / 60 + dms[2].ToDouble() / 3600;
            var reference = profile.Values.FirstOrDefault(v => v.Tag.ToString() == key + "Ref")?.GetValue() as string;
            var text = degrees.ToString("0.0000", CultureInfo.InvariantCulture) + "°";
            return string.IsNullOrWhiteSpace(reference) ? text : $"{text} {reference.Trim()}";
        }
        var described = Text(raw);
        return key == "GPSAltitude" && raw is Rational ? described + " m" : described;
    }

    static string Text(object? raw) => raw switch
    {
        null => "",
        string s => s.Trim('\0', ' '),
        Rational r => Fraction(r.Numerator, r.Denominator, () => r.ToDouble()),
        SignedRational r => Fraction(r.Numerator, r.Denominator, () => r.ToDouble()),
        byte[] b => b.Length is > 0 and <= 64 && b.All(c => c is >= 0x20 and < 0x7F or 0)
            ? System.Text.Encoding.ASCII.GetString(b).TrimEnd('\0', ' ')
            : $"{b.Length}바이트 데이터",
        Array a => string.Join(", ", a.Cast<object?>().Select(Text)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => raw.ToString() ?? "",
    };

    static string Fraction(long numerator, long denominator, Func<double> value) =>
        denominator == 0 ? "" :
        denominator == 1 ? numerator.ToString(CultureInfo.InvariantCulture) :
        Math.Abs(numerator) == 1 ? $"{numerator}/{denominator}" :
        value().ToString("0.####", CultureInfo.InvariantCulture);

    // ---- Write ----

    public static string Write(string source, MetadataEdit edit, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var format = FormatCatalog.FormatOf(source);
        if (format is null || !Writable.Contains(format) || !CapabilityProbe.CanWrite(format)) throw new ZestException(NotEditable);

        // XMP repeats EXIF (dc:creator, photoshop:DateCreated, tiff:Make...), so a removed or changed EXIF value would survive
        // there; and MPF secondary images carry their own EXIF. Any EXIF edit drops both (docs/decisions.md F1).
        var exifEdited = edit.Removed.Any(IsExifId) || edit.Changed.Keys.Any(IsExifId);
        var dropXmp = edit.RemoveAll || edit.RemoveLocation || exifEdited || edit.Removed.Contains(XmpId);
        var dropIptc = edit.RemoveAll || edit.Removed.Contains(IptcId);
        var tag = edit.RemoveAll ? "no metadata" : "edited";
        return format == "jpg"
            ? WriteJpeg(source, edit, dropXmp, dropIptc, exifEdited, tag, ctx)
            : WriteThroughMagick(source, format, edit, dropXmp, dropIptc, tag, ctx);
    }

    static bool IsExifId(string id) => id.StartsWith("exif:", StringComparison.Ordinal);

    static string WriteJpeg(string source, MetadataEdit edit, bool dropXmp, bool dropIptc, bool exifEdited, string tag, OperationContext ctx)
    {
        var bytes = File.ReadAllBytes(source);
        byte[]? payload;
        try
        {
            using var image = new MagickImage();
            image.Ping(bytes);
            var parsed = ExifOf(image, out var raw);
            var exif = BuildExif(parsed, edit, out var changed);
            // Untouched EXIF goes back as it was stored (unknown tags and maker notes included) ...
            payload = changed ? exif?.ToByteArray() : raw;
            // ... unless it could not be parsed and a location removal was asked for: it may hold one.
            if (!changed && edit.RemoveLocation && parsed is not null && !parsed.Values.Any()) payload = null;
        }
        catch (Exception e) when (e is MagickException or ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new ZestException(Unreadable);
        }
        if (payload is not null) payload = WithExifHeader(payload);
        ctx.Report(0.4);

        var cleanup = edit.RemoveAll ? JpegCleanup.Everything : edit.RemoveLocation || exifEdited ? JpegCleanup.Location : JpegCleanup.None;
        var rewritten = JpegSegments.Rewrite(bytes, payload, dropXmp, dropIptc, cleanup);
        ctx.Token.ThrowIfCancellationRequested();
        var output = OutputNaming.ToolOutput(source, tag, "jpg", ctx);
        File.WriteAllBytes(output, rewritten);
        ctx.Report(1);
        return output;
    }

    static string WriteThroughMagick(string source, string format, MetadataEdit edit, bool dropXmp, bool dropIptc, string tag, OperationContext ctx)
    {
        using var image = ImageConverter.Load(source, format, singleFrameOnly: true); // no AutoOrient: the pixels stay as stored
        ctx.Report(0.4);
        try
        {
            var exif = BuildExif(image.GetExifProfile(), edit, out var changed);
            if (edit.RemoveAll)
            {
                var icc = image.GetColorProfile();
                image.Strip(); // every profile and text chunk
                if (icc is not null) image.SetProfile(icc);
                if (format == "png") image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
            }
            else
            {
                if (dropXmp) image.RemoveProfile("xmp");
                if (dropIptc) { image.RemoveProfile("iptc"); image.RemoveProfile("8bim"); }
            }
            if (changed)
            {
                if (exif is null) image.RemoveProfile("exif");
                else image.SetProfile(exif);
            }
        }
        catch (Exception e) when (e is MagickException or ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new ZestException(Unreadable);
        }

        ctx.Token.ThrowIfCancellationRequested();
        var output = OutputNaming.ToolOutput(source, tag, format, ctx);
        try
        {
            ImageConverter.Encode(image, format, output, lossyQuality: 97);
        }
        catch (MagickException)
        {
            throw new ZestException("이미지를 저장하지 못했습니다.");
        }
        ctx.Report(1);
        return output;
    }

    /// <summary>
    /// The EXIF to write: the source's with <see cref="MetadataEdit.Changed"/> applied, then <see cref="MetadataEdit.Removed"/>
    /// and (for <see cref="MetadataEdit.RemoveLocation"/>) every GPS tag taken out; for <see cref="MetadataEdit.RemoveAll"/> only
    /// Orientation (when not 1). Null means no EXIF at all. <paramref name="changed"/> is false when the source EXIF stays as it is.
    /// </summary>
    internal static IExifProfile? BuildExif(IExifProfile? source, MetadataEdit edit, out bool changed)
    {
        if (edit.RemoveAll)
        {
            changed = true;
            var orientation = source?.GetValue(ExifTag.Orientation)?.Value;
            if (orientation is not (>= 2 and <= 8)) return null; // 1 is upright; 0 and others mean nothing
            var kept = new ExifProfile();
            kept.SetValue(ExifTag.Orientation, orientation.Value);
            return kept;
        }

        changed = false;
        IExifProfile profile = source ?? new ExifProfile();
        foreach (var (id, value) in edit.Changed)
        {
            if (!id.StartsWith("exif:", StringComparison.Ordinal)) continue;
            var key = id[5..];
            if (MetadataLabels.IsLocationKey("exif", key) || !TextTags.TryGetValue(key, out var textTag)) continue; // read-only
            if (string.IsNullOrWhiteSpace(value))
            {
                changed |= profile.RemoveValue(textTag);
            }
            else if (profile.GetValue(textTag)?.Value != value)
            {
                profile.SetValue(textTag, value);
                changed = true;
            }
        }
        foreach (var value in profile.Values.ToList())
        {
            var key = value.Tag.ToString()!;
            if (edit.Removed.Contains($"exif:{key}") || (edit.RemoveLocation && MetadataLabels.IsLocationKey("exif", key)))
                changed |= profile.RemoveValue(value.Tag);
        }
        return profile.Values.Any() ? profile : null;
    }

    /// <summary>
    /// The image's EXIF, parsed after normalizing its header: ImageMagick accepts any "exif" header ("Exif\0\xFF"...), while
    /// Magick.NET only parses "Exif\0\0". <paramref name="raw"/> is the normalized stored bytes (null when there is no EXIF).
    /// </summary>
    static ExifProfile? ExifOf(MagickImage image, out byte[]? raw)
    {
        raw = image.GetProfile("exif")?.ToByteArray() is { Length: > 0 } stored ? WithExifHeader(stored) : null;
        return raw is null ? null : new ExifProfile(raw);
    }

    /// <summary>The APP1 body with exactly <c>Exif\0\0</c> in front: a variant 6-byte "exif" header is replaced, a missing one added.</summary>
    static byte[] WithExifHeader(byte[] data)
    {
        if (data.AsSpan().StartsWith("Exif\0\0"u8)) return data;
        if (data.Length >= 6 && System.Text.Ascii.EqualsIgnoreCase(data.AsSpan(0, 4), "exif"u8)) return [.. "Exif\0\0"u8, .. data.AsSpan(6)];
        return [.. "Exif\0\0"u8, .. data];
    }
}
