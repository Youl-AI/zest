namespace Zest.Core.Metadata;

/// <summary>
/// Rewrites the metadata segments of a JPEG without touching the compressed picture: every segment before SOS is
/// parsed, the EXIF/XMP/IPTC ones are replaced or dropped, and everything from SOS to the end is copied byte for byte.
/// </summary>
internal static class JpegSegments
{
    const byte Soi = 0xD8, Sos = 0xDA, Eoi = 0xD9, App0 = 0xE0, App1 = 0xE1, App13 = 0xED;

    static readonly byte[] ExifId = "Exif\0\0"u8.ToArray();
    static readonly byte[] XmpId = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();
    static readonly byte[] XmpExtensionId = "http://ns.adobe.com/xmp/extension/\0"u8.ToArray();

    const string Unreadable = "메타데이터를 읽을 수 없습니다.";

    /// <param name="exifApp1Payload">The new APP1 body, starting with <c>Exif\0\0</c>; null leaves the file without EXIF.</param>
    /// <param name="dropXmp">Drops the XMP packet (and any extended-XMP parts).</param>
    /// <param name="dropIptc">Drops APP13 (Photoshop IRB, which holds IPTC).</param>
    public static byte[] Rewrite(byte[] jpeg, byte[]? exifApp1Payload, bool dropXmp, bool dropIptc)
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
                output.Write(jpeg, pos, jpeg.Length - pos);
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
            var body = jpeg.AsSpan(pos + 4, length - 2);

            if (!inserted && marker != App0) // the new EXIF goes right after SOI and the JFIF/JFXX headers
            {
                WriteApp1(output, exifApp1Payload!);
                inserted = true;
            }
            var drop = marker switch
            {
                App1 when body.StartsWith(ExifId) => true,
                App1 when body.StartsWith(XmpId) || body.StartsWith(XmpExtensionId) => dropXmp,
                App13 => dropIptc,
                _ => false,
            };
            if (!drop) output.Write(jpeg, pos, 2 + length);
            pos += 2 + length;
        }
    }

    static void WriteApp1(MemoryStream output, byte[] payload)
    {
        var length = payload.Length + 2;
        output.Write([0xFF, App1, (byte)(length >> 8), (byte)length]);
        output.Write(payload);
    }
}
