using LimeWin.App.Jobs;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;

namespace LimeWin.App;

/// <summary>App-wide services, built once at startup on the UI thread.</summary>
public static class Services
{
    static Capabilities? _capabilities;
    static ConversionRouter? _router;
    static JobRunner? _jobs;

    public static Capabilities Capabilities => _capabilities ?? throw NotStarted();
    public static ConversionRouter Router => _router ?? throw NotStarted();
    public static JobRunner Jobs => _jobs ?? throw NotStarted();

    public static void Start()
    {
        _capabilities = CapabilityDetector.Detect();
        List<IConverter> converters = [new ImageConverter(), new PdfConverter()];
        if (_capabilities.Ffmpeg && FfmpegLocator.Locate() is { } ffmpeg)
            converters.Add(new FfmpegConverter(ffmpeg));
        _router = new ConversionRouter(converters);
        _jobs = new JobRunner(new ProgressCardHost());
    }

    static InvalidOperationException NotStarted() => new("Services.Start() has not run.");
}
