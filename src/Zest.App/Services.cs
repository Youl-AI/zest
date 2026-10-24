using Zest.App.Jobs;
using Zest.Core.Catalog;
using Zest.Core.Convert;
using Zest.Core.Office;

namespace Zest.App;

/// <summary>App-wide services, built once at startup on the UI thread.</summary>
public static class Services
{
    static Capabilities? _capabilities;
    static ConversionRouter? _router;
    static JobRunner? _jobs;
    static OfficeHost? _word, _powerPoint, _excel;

    public static Capabilities Capabilities => _capabilities ?? throw NotStarted();
    public static ConversionRouter Router => _router ?? throw NotStarted();
    public static JobRunner Jobs => _jobs ?? throw NotStarted();

    public static void Start()
    {
        _capabilities = CapabilityDetector.Detect();
        // Word is always registered so md<->html works without Office; FormatCatalog hides the routes that need Word.
        // One shared host per Office app; each starts its thread and app only on its first conversion.
        _word = new OfficeHost("Word.Application", "WINWORD");
        if (_capabilities.Office)
        {
            _powerPoint = new OfficeHost("PowerPoint.Application", "POWERPNT");
            _excel = new OfficeHost("Excel.Application", "EXCEL");
        }
        var converters = ConverterSet.Create(_capabilities, _word, _powerPoint, _excel);
        _router = new ConversionRouter(converters);
        _jobs = new JobRunner(new ProgressCardHost());
    }

    static readonly TimeSpan JobCleanupWait = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Called on app exit (UI thread): cancels running jobs and gives their clean-up up to 3 s, then releases the Office
    /// apps side by side (each quits if we started it and it holds no document; each may wait for a stuck call).
    /// </summary>
    public static void Stop()
    {
        _jobs?.CancelAll(JobCleanupWait);
        var hosts = new[] { _word, _powerPoint, _excel }.OfType<OfficeHost>().ToArray();
        _word = _powerPoint = _excel = null;
        try { Task.WaitAll([.. hosts.Select(h => Task.Run(h.Dispose))]); }
        catch (AggregateException) { /* each host released what it could; the process is ending */ }
    }

    static InvalidOperationException NotStarted() => new("Services.Start() has not run.");
}
