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

    /// <summary>Releases Office (quits each app we started if it has no open document). Called on app exit.</summary>
    public static void Stop()
    {
        _word?.Dispose();
        _powerPoint?.Dispose();
        _excel?.Dispose();
        _word = _powerPoint = _excel = null;
    }

    static InvalidOperationException NotStarted() => new("Services.Start() has not run.");
}
