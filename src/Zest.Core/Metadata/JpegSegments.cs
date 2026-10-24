using System.Text;

namespace Zest.Core.Metadata;

/// <summary>How much more than EXIF/XMP/IPTC a rewrite takes out of a JPEG.</summary>
internal enum JpegCleanup
{
    /// <summary>Edits that leave EXIF alone (only XMP or IPTC struck): every other segment and everything after SOS stay as they are.</summary>
    None,
    /// <summary>
    /// 위치 지우기 and any EXIF edit: also APP11 (JUMBF/C2PA), APP2 MPF and whatever follows the primary image's EOI (gain maps,
    /// MPF images, which carry their own EXIF).
    /// </summary>
    Location,
    /// <summary>태그 모두 지우기: <see cref="Location"/> plus COM, every APP segment except APP0, APP2 ICC and APP14, and APP13.</summary>
    Everything,
}

/// <summary>
/// Rewrites the metadata segments of a JPEG without touching the compressed picture: segments are parsed, the EXIF/XMP/IPTC
/// ones (and, with a <see cref="JpegCleanup"/>, vendor segments and trailing data) are replaced or dropped, and the
/// entropy-coded data is copied byte for byte.
/// </summary>
internal static class JpegSegments
{
    const byte Soi = 0xD8, Sos = 0xDA, Eoi = 0xD9, App0 = 0xE0, App1 = 0xE1, App2 = 0xE2, App11 = 0xEB, App13 = 0xED,
        App14 = 0xEE, App15 = 0xEF, Com = 0xFE;

    static readonly byte[] XmpId = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();
    static readonly byte[] XmpExtensionId = "http://ns.adobe.com/xmp/extension/\0"u8.ToArray();
    static readonly byte[] IccId = "ICC_PROFILE\0"u8.ToArray();
    static readonly byte[] MpfId = "MPF\0"u8.ToArray();

    const string Unreadable = "메타데이터를 읽을 수 없습니다.";

    /// <param name="exifApp1Payload">The new APP1 body, starting with <c>Exif\0\0</c>; null leaves the file without EXIF.</param>
    /// <param name="dropXmp">Drops the XMP packet (and any extended-XMP parts).</param>
    /// <param name="dropIptc">Drops APP13 (Photoshop IRB, which holds IPTC).</param>
    public static byte[] Rewrite(byte[] jpeg, byte[]? exifApp1Payload, bool dropXmp, bool dropIptc, JpegCleanup cleanup = JpegCleanup.None)
    {
        if (exifApp1Payload is not null && exifApp1Payload.Length + 2 > ushort.MaxValue)
            throw new ZestException("메타데이터가 너무 커서 저장할 수 없습니다.");
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != Soi) throw new ZestException(Unreadable);

        using var output = new MemoryStream(jpeg.Length + (exifApp1Payload?.Length ?? 0) + 4);
        output.Write([0xFF, Soi]);
        var inserted = exifApp1Payload is null;
        var pos = 2;
        while (true)
        {
            if (pos + 1 >= jpeg.Length || jpeg[pos] != 0xFF) throw new ZestException(Unreadable);
            var marker = jpeg[pos + 1];
            if (marker == 0xFF) { pos++; continue; } // fill byte before a marker
            if (marker == Sos)
            {
                if (!inserted) WriteApp1(output, exifApp1Payload!);
                if (cleanup == JpegCleanup.None) output.Write(jpeg, pos, jpeg.Length - pos);
                else CopyScansToEoi(jpeg, pos, output, dropXmp, dropIptc, cleanup);
                return output.ToArray();
            }
            if (marker is Eoi or Soi or 0x00) throw new ZestException(Unreadable); // no picture before the end
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) // TEM and RSTn stand alone
            {
                output.Write(jpeg, pos, 2);
                pos += 2;
                continue;
            }
            if (pos + 3 >= jpeg.Length) throw new ZestException(Unreadable);
            var length = jpeg[pos + 2] << 8 | jpeg[pos + 3];
            if (length < 2 || pos + 2 + length > jpeg.Length) throw new ZestException(Unreadable);

            if (!inserted && marker != App0) // the new EXIF goes right after SOI and the JFIF/JFXX headers
            {
                WriteApp1(output, exifApp1Payload!);
                inserted = true;
            }
            if (!Drops(marker, jpeg.AsSpan(pos + 4, length - 2), dropXmp, dropIptc, cleanup)) output.Write(jpeg, pos, 2 + length);
            pos += 2 + length;
        }
    }

    /// <summary>
    /// Copies from the first SOS up to and including the primary image's EOI, walking the scans so that segments between
    /// them follow the same rules; nothing after EOI is copied. Entropy-coded data holds no unstuffed FF other than RSTn.
    /// A file that ends before EOI (truncated) is copied to its end.
    /// </summary>
    static void CopyScansToEoi(byte[] jpeg, int pos, MemoryStream output, bool dropXmp, bool dropIptc, JpegCleanup cleanup)
    {
        var copyFrom = pos;
        while (pos + 1 < jpeg.Length)
        {
            if (jpeg[pos] != 0xFF) { pos++; continue; }
            var marker = jpeg[pos + 1];
            if (marker == 0xFF) { pos++; continue; }
            if (marker is 0x00 or 0x01 or >= 0xD0 and <= 0xD7) { pos += 2; continue; } // stuffed FF, TEM, RSTn
            if (marker == Eoi)
            {
                output.Write(jpeg, copyFrom, pos + 2 - copyFrom);
                return;
            }
            if (pos + 3 >= jpeg.Length) break;
            var length = jpeg[pos + 2] << 8 | jpeg[pos + 3];
            if (length < 2 || pos + 2 + length > jpeg.Length) break;
            if (Drops(marker, jpeg.AsSpan(pos + 4, length - 2), dropXmp, dropIptc, cleanup))
            {
                output.Write(jpeg, copyFrom, pos - copyFrom);
                copyFrom = pos + 2 + length;
            }
            pos += 2 + length;
        }
        output.Write(jpeg, copyFrom, jpeg.Length - copyFrom);
    }

    static bool Drops(byte marker, ReadOnlySpan<byte> body, bool dropXmp, bool dropIptc, JpegCleanup cleanup)
    {
        var everything = cleanup == JpegCleanup.Everything;
        var location = cleanup != JpegCleanup.None;
        return marker switch
        {
            // ImageMagick takes any APP1 starting with "exif" (any case) as the EXIF profile; so does this.
            App1 when body.Length >= 4 && Ascii.EqualsIgnoreCase(body[..4], "exif"u8) => true,
            App1 when body.StartsWith(XmpId) || body.StartsWith(XmpExtensionId) => dropXmp || everything,
            App1 => everything,
            App2 when body.StartsWith(IccId) => false,
            App2 when body.StartsWith(MpfId) => location,
            App2 => everything,
            App11 => location,
            App13 => dropIptc || everything,
            App0 or App14 => false,
            >= 0xE3 and <= App15 or Com => everything,
            _ => false, // DQT, DHT, SOF, DRI and every other non-APP marker
        };
    }

    static void WriteApp1(MemoryStream output, byte[] payload)
    {
        var length = payload.Length + 2;
        output.Write([0xFF, App1, (byte)(length >> 8), (byte)length]);
        output.Write(payload);
    }
}
