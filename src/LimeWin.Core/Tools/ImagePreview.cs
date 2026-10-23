using ImageMagick;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;

namespace LimeWin.Core.Tools;

/// <summary>A downscaled copy of an image for display, in straight (not premultiplied) BGRA, rows top to bottom.</summary>
/// <param name="SourceWidth">Width of the full image as the tools see it (EXIF orientation applied).</param>
public sealed record PreviewImage(int SourceWidth, int SourceHeight, int Width, int Height, byte[] Bgra);

/// <summary>Decodes images for the tool windows with the same loader and orientation the tools use.</summary>
public static class ImagePreview
{
    /// <param name="maxLongEdge">The preview's long edge never exceeds this; smaller images are not enlarged.</param>
    /// <param name="singleFrameOnly">True for the crop editor, so an animated source fails here exactly as the cropper would.</param>
    public static PreviewImage Load(string source, int maxLongEdge, bool singleFrameOnly = false)
    {
        using var image = ImageConverter.LoadOriented(source, FormatCatalog.FormatOf(source), singleFrameOnly);
        int sourceW = (int)image.Width, sourceH = (int)image.Height;
        var edge = (uint)Math.Max(1, maxLongEdge);
        if (Math.Max(image.Width, image.Height) > edge) image.Thumbnail(new MagickGeometry(edge, edge)); // fits inside, aspect kept
        try
        {
            using var pixels = image.GetPixels();
            var bgra = pixels.ToByteArray(PixelMapping.BGRA) ?? throw new LimeException("이미지를 읽을 수 없습니다.");
            return new PreviewImage(sourceW, sourceH, (int)image.Width, (int)image.Height, bgra);
        }
        catch (MagickException)
        {
            throw new LimeException("이미지를 읽을 수 없습니다.");
        }
    }
}
