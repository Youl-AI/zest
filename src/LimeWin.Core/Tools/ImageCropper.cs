using ImageMagick;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;

namespace LimeWin.Core.Tools;

/// <summary>A crop rectangle in pixels of the image as the user sees it (EXIF orientation applied).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Cuts a rectangle out of an image; the result is `name (cropped).ext` next to the source, in the same format.</summary>
public static class ImageCropper
{
    public static Task<string> CropAsync(string source, PixelRect rect, OperationContext ctx) =>
        ToolRunner.RunAsync(ctx, () => Task.Run(() => Crop(source, rect, ctx), ctx.Token));

    static string Crop(string source, PixelRect rect, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var format = FormatCatalog.FormatOf(source);
        if (format is null || format is "svg" || FormatCatalog.CategoryOf(format) != FormatCategory.Image || !CapabilityProbe.CanWrite(format))
            throw new LimeException("이 형식은 자를 수 없습니다.");

        using var image = ImageConverter.LoadOriented(source, format, singleFrameOnly: true);
        ctx.Report(0.4);

        long x0 = Math.Max(0, (long)rect.X), y0 = Math.Max(0, (long)rect.Y);
        long x1 = Math.Min(image.Width, (long)rect.X + rect.Width), y1 = Math.Min(image.Height, (long)rect.Y + rect.Height);
        if (x1 - x0 < 1 || y1 - y0 < 1) throw new LimeException("자를 영역이 비어 있습니다.");

        image.Crop(new MagickGeometry((int)x0, (int)y0, (uint)(x1 - x0), (uint)(y1 - y0)));
        image.ResetPage();

        ctx.Token.ThrowIfCancellationRequested();
        var output = OutputNaming.ToolOutput(source, "cropped", format);
        ctx.Track(output);
        try
        {
            ImageConverter.Encode(image, format, output, lossyQuality: 92); // a crop should not visibly degrade the picture
        }
        catch (MagickException)
        {
            throw new LimeException("이미지를 저장하지 못했습니다.");
        }
        ctx.Report(1);
        return output;
    }
}
