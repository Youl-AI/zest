using System.Collections.Concurrent;
using ImageMagick;

namespace LimeWin.Core.Convert;

public static class CapabilityProbe
{
    static readonly ConcurrentDictionary<string, bool> Cache = new();

    /// <summary>True when Magick.NET can encode a 1x1 image in the given format.</summary>
    public static bool CanWrite(string imageFormat) =>
        Cache.GetOrAdd(imageFormat.ToLowerInvariant(), static f =>
        {
            try
            {
                using var image = new MagickImage(MagickColors.White, 1, 1);
                using var ms = new MemoryStream();
                image.Write(ms, ImageConverter.MagickFormatOf(f));
                return ms.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        });
}
