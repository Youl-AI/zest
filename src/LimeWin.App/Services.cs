using LimeWin.App.Jobs;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Office;

namespace LimeWin.App;

/// <summary>App-wide services, built once at startup on the UI thread.</summary>
public static class Services
{
    static Capabilities? _capabilities;
    static ConversionRouter? _router;
    static JobRunner? _jobs;
    static OfficeHost? _word;

    public static Capabilities Capabilities => _capabilities ?? throw NotStarted();
    public static ConversionRouter Router => _router ?? throw NotStarted();
    public static JobRunner Jobs => _jobs ?? throw NotStarted();

    public static void Start()
    {
        _capabilities = CapabilityDetector.Detect();
        List<IConverter> converters = [new ImageConverter(), new PdfConverter()];
        if (_capabilities.Ffmpeg && FfmpegLocator.Locate() is { } ffmpeg)
            converters.Add(new FfmpegConverter(ffmpeg));
        // Always registered so md<->html works without Office; FormatCatalog hides the routes that need Word.
        // One shared Word; the host starts its thread and Word itself only on the first Word conversion.
        _word = new OfficeHost("Word.Application", "WINWORD");
        converters.Add(new WordConverter(_word));
        _router = new ConversionRouter(converters);
        _jobs = new JobRunner(new ProgressCardHost());
    }

    /// <summary>Releases Office (quits the Word we started if it has no open document). Called on app exit.</summary>
    public static void Stop()
    {
        _word?.Dispose();
        _word = null;
    }

    static InvalidOperationException NotStarted() => new("Services.Start() has not run.");
}
