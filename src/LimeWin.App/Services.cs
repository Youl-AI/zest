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
        _router = new ConversionRouter([new ImageConverter(), new PdfConverter()]);
        _jobs = new JobRunner(new ProgressCardHost());
    }

    static InvalidOperationException NotStarted() => new("Services.Start() has not run.");
}
