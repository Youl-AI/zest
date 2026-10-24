using Zest.Core.Catalog;
using Zest.Core.Office;

namespace Zest.Core.Convert;

/// <summary>The converters the app registers for a machine; shared by the app and the route-matrix test.</summary>
public static class ConverterSet
{
    /// <summary>
    /// Word is always registered (md and html convert without Office; the catalog hides the routes that need Word).
    /// PowerPoint and Excel only with Office capability and both hosts given; ffmpeg only when it is available.
    /// </summary>
    public static List<IConverter> Create(Capabilities caps, OfficeHost word, OfficeHost? powerPoint, OfficeHost? excel)
    {
        List<IConverter> converters = [new ImageConverter(), new PdfConverter()];
        if (caps.Ffmpeg && FfmpegLocator.Locate() is { } ffmpeg)
            converters.Add(new FfmpegConverter(ffmpeg));
        converters.Add(new WordConverter(word));
        if (caps.Office && powerPoint is not null && excel is not null)
        {
            converters.Add(new PowerPointConverter(powerPoint));
            converters.Add(new ExcelConverter(excel));
        }
        return converters;
    }
}
