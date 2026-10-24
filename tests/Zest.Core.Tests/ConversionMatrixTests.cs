using System.Security.Cryptography;
using Zest.Core.Catalog;
using Zest.Core.Convert;
using Zest.Core.Jobs;
using Zest.Core.Office;
using Zest.Core.Tests.Fixtures;

namespace Zest.Core.Tests;

/// <summary>
/// Test classes that start Office or ffmpeg share this collection so they never run in parallel: Office hosts tell
/// "our" process from the user's by process snapshots, and the matrix checks that no ffmpeg is left over.
/// </summary>
public static class ProcessCollection
{
    public const string Name = "External processes";
}

[CollectionDefinition(ProcessCollection.Name)]
public sealed class ProcessCollectionDefinition;

/// <summary>
/// One Word, PowerPoint and Excel host plus the router built exactly like the app builds it
/// (<see cref="ConverterSet.Create"/>), shared by the whole matrix so Office starts once per app.
/// </summary>
public sealed class MatrixFixture : IDisposable
{
    public Capabilities Caps { get; } = CapabilityDetector.Detect();
    public OfficeHost Word { get; } = new("Word.Application", "WINWORD");
    public OfficeHost PowerPoint { get; } = new("PowerPoint.Application", "POWERPNT");
    public OfficeHost Excel { get; } = new("Excel.Application", "EXCEL");
    public ConversionRouter Router { get; }
    readonly string _cache = TestFiles.TempDir();
    readonly Dictionary<string, string> _masters = [];

    public MatrixFixture() => Router = new ConversionRouter(ConverterSet.Create(Caps, Word, PowerPoint, Excel));

    /// <summary>A fresh copy of the format's fixture, named <c>한글 파일 (1).format</c>, in <paramref name="dir"/>.</summary>
    public async Task<string> SourceAsync(string format, string dir)
    {
        if (!_masters.TryGetValue(format, out var master))
        {
            var masterDir = Path.Combine(_cache, format);
            Directory.CreateDirectory(masterDir);
            _masters[format] = master = await FixtureFactory.For(masterDir, format, Word, PowerPoint, Excel);
        }
        var copy = Path.Combine(dir, Path.GetFileName(master));
        File.Copy(master, copy);
        return copy;
    }

    public void Dispose()
    {
        OfficeProcess?[] launched = [Word.LaunchedProcess, PowerPoint.LaunchedProcess, Excel.LaunchedProcess];
        Word.Dispose();
        PowerPoint.Dispose();
        Excel.Dispose();
        try { Directory.Delete(_cache, true); } catch (IOException) { }
        var alive = launched.Where(p => p is { } x && OfficeProcesses.IsAlive(x)).Select(p => p!.Value.Id).ToList();
        if (alive.Count > 0)
            throw new InvalidOperationException($"Office started by the matrix is still running after Dispose: PID {string.Join(", ", alive)}");
    }
}

/// <summary>Every (source, target) pair the catalog offers on this machine, run for real.</summary>
[Trait("Category", "Matrix")]
[Collection(ProcessCollection.Name)]
public class ConversionMatrixTests(MatrixFixture fx, ITestOutputHelper output) : IClassFixture<MatrixFixture>
{
    static readonly string[] Sources =
    [
        "jpg", "png", "webp", "heic", "tiff", "avif", "bmp", "svg", "gif",
        "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma",
        "mp4", "mov", "mkv", "webm", "avi", "wmv",
        "pdf", "docx", "doc", "rtf", "odt", "txt", "md", "html",
        "ppt", "pptx", "odp", "xlsx", "xls", "ods", "csv", "tsv",
    ];

    static readonly Capabilities Caps = CapabilityDetector.Detect();

    enum Group { Plain, Ffmpeg, Office }

    static Group GroupOf(string source, string target) => FormatCatalog.CategoryOf(source) switch
    {
        FormatCategory.Gif or FormatCategory.Audio or FormatCategory.Video => Group.Ffmpeg,
        FormatCategory.Slides or FormatCategory.Spreadsheet => Group.Office,
        FormatCategory.Document when (source, target) is not (("md", "html") or ("html", "md")) => Group.Office,
        _ => Group.Plain,
    };

    static IEnumerable<(string From, string To)> Routes() =>
        Sources.SelectMany(s => FormatCatalog.TargetsFor(s, Caps).Select(t => (s, t)));

    static TheoryData<string, string> Of(Group group)
    {
        var data = new TheoryData<string, string>();
        foreach (var (from, to) in Routes().Where(r => GroupOf(r.From, r.To) == group)) data.Add(from, to);
        return data;
    }

    public static TheoryData<string, string> PlainRoutes => Of(Group.Plain);
    public static TheoryData<string, string> FfmpegRoutes => Of(Group.Ffmpeg);
    public static TheoryData<string, string> OfficeRoutes => Of(Group.Office);

    [Fact]
    public void ReportRouteCounts()
    {
        var all = Routes().ToList();
        output.WriteLine($"capabilities: {Caps}");
        output.WriteLine($"routes total: {all.Count}");
        foreach (var g in Enum.GetValues<Group>())
            output.WriteLine($"  {g}: {all.Count(r => GroupOf(r.From, r.To) == g)}");
        foreach (var c in Enum.GetValues<FormatCategory>())
            output.WriteLine($"  source category {c}: {all.Count(r => FormatCatalog.CategoryOf(r.From) == c)}");
        // Every catalog source is covered; a new input format must be added to Sources.
        Assert.Equal(Sources.Length, Sources.Distinct().Count());
        Assert.Equal(FormatCatalog.InputFormats.Order(), Sources.Order());
        Assert.NotEmpty(all);
    }

    [Theory, MemberData(nameof(PlainRoutes))]
    public Task Plain(string from, string to) => RunAsync(from, to);

    [Theory, MemberData(nameof(FfmpegRoutes)), Trait("Category", "Ffmpeg")]
    public Task Ffmpeg(string from, string to) => RunAsync(from, to);

    [Theory, MemberData(nameof(OfficeRoutes)), Trait("Category", "Office")]
    public Task Office(string from, string to) => RunAsync(from, to);

    async Task RunAsync(string from, string to)
    {
        if (from == "heic" && !Caps.HeicWrite) Assert.Skip("HEIC cannot be written here, so there is no fixture source.");
        var dir = TestFiles.TempDir();
        try
        {
            var src = await fx.SourceAsync(from, dir);
            var hashBefore = Hash(src);
            var ffmpegBefore = OfficeProcesses.Snapshot("ffmpeg");
            var (officeName, officeHost) = OfficeOf(from, to);
            var officeBefore = officeName is null ? null : OfficeProcesses.Snapshot(officeName);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var results = await fx.Router.ConvertAsync(src, to, new OperationContext(cts.Token));

            Assert.NotEmpty(results);
            foreach (var r in results)
            {
                if (Directory.Exists(r))
                    Assert.True(Directory.EnumerateFileSystemEntries(r).Any(), $"{from}->{to}: empty folder {r}");
                else
                {
                    Assert.True(File.Exists(r), $"{from}->{to}: missing {r}");
                    Assert.True(new FileInfo(r).Length > 0, $"{from}->{to}: empty file {r}");
                }
                Assert.Equal(dir, Path.GetDirectoryName(r));
            }

            Assert.Equal(hashBefore, Hash(src));
            using (File.Open(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } // not locked afterwards

            var stray = Wait(() => OfficeProcesses.Added(ffmpegBefore, OfficeProcesses.Snapshot("ffmpeg")));
            Assert.True(stray.Count == 0, $"{from}->{to}: ffmpeg still running: {string.Join(", ", stray.Select(p => p.Id))}");

            if (officeBefore is not null)
            {
                // The shared host's own app may stay alive (it idles out later); anything else this case started leaked.
                var leaked = OfficeProcesses.Added(officeBefore, OfficeProcesses.Snapshot(officeName!))
                    .Where(p => p != officeHost!.LaunchedProcess).ToList();
                Assert.True(leaked.Count == 0, $"{from}->{to}: {officeName} left running: {string.Join(", ", leaked.Select(p => p.Id))}");
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    (string? Process, OfficeHost? Host) OfficeOf(string from, string to) => GroupOf(from, to) != Group.Office ? (null, null)
        : FormatCatalog.CategoryOf(from) switch
        {
            FormatCategory.Slides => ("POWERPNT", fx.PowerPoint),
            FormatCategory.Spreadsheet => ("EXCEL", fx.Excel),
            _ => ("WINWORD", fx.Word),
        };

    static string Hash(string path) => System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    static IReadOnlyList<OfficeProcess> Wait(Func<IReadOnlyList<OfficeProcess>> probe)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        IReadOnlyList<OfficeProcess> left;
        while ((left = probe()).Count > 0 && DateTime.UtcNow < deadline) Thread.Sleep(100);
        return left;
    }
}
