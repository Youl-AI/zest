using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Office;
using LimeWin.Core.Pdf;
using LimeWin.Core.Tests.Fixtures;

namespace LimeWin.Core.Tests;

public class ExcelConverterHandlesTests
{
    [Fact]
    public void Handles_SheetRoutes()
    {
        using var host = new OfficeHost("Excel.Application", "EXCEL"); // never started
        var c = new ExcelConverter(host);
        Assert.True(c.Handles("xls", "xlsx"));
        Assert.True(c.Handles("csv", "tsv"));
        Assert.True(c.Handles("ods", "pdf"));
        Assert.False(c.Handles("xlsx", "xlsx"));
        Assert.False(c.Handles("xlsx", "xls")); // input only
        Assert.False(c.Handles("docx", "xlsx"));
    }
}

/// <summary>One Excel for the whole class; its disposal must leave no EXCEL.EXE we started.</summary>
public sealed class ExcelFixture : IDisposable
{
    public OfficeHost Excel { get; } = new("Excel.Application", "EXCEL");
    readonly string _cache = TestFiles.TempDir();
    readonly Dictionary<string, string> _masters = [];

    /// <summary>A fresh copy of the A1="이름", B2=42 fixture in <paramref name="format"/>, named <c>가계부 (1).format</c> in <paramref name="dir"/>.</summary>
    public async Task<string> SourceAsync(string format, string dir)
    {
        if (!_masters.TryGetValue(format, out var master))
            _masters[format] = master = await FixtureFactory.Sheet(Excel, _cache, $"master.{format}");
        var copy = Path.Combine(dir, $"가계부 (1).{format}");
        File.Copy(master, copy);
        return copy;
    }

    /// <summary>Sheet name, A1 and B2 of the first worksheet of a workbook Excel can open.</summary>
    public Task<(string Sheet, object A1, object B2)> ReadFirstSheetAsync(string path) => Excel.RunAsync(app =>
    {
        dynamic books = app.Workbooks;
        dynamic book = books.Open(Filename: path, ReadOnly: true, AddToMru: false);
        try
        {
            dynamic sheets = book.Worksheets;
            dynamic first = sheets.Item(1);
            (string, object, object) cells = ((string)first.Name, (object)first.Range("A1").Value2, (object)first.Range("B2").Value2);
            Marshal.FinalReleaseComObject(first);
            Marshal.FinalReleaseComObject(sheets);
            return cells;
        }
        finally
        {
            book.Close(SaveChanges: false);
            Marshal.FinalReleaseComObject(book);
            Marshal.FinalReleaseComObject(books);
        }
    }, TimeSpan.FromMinutes(1), CancellationToken.None);

    public void Dispose()
    {
        var launched = Excel.LaunchedProcess;
        Excel.Dispose();
        try { Directory.Delete(_cache, true); } catch (IOException) { }
        if (launched is { } p && OfficeProcesses.IsAlive(p))
            throw new InvalidOperationException($"EXCEL PID {p.Id} started by the test host is still running after Dispose.");
    }
}

[Trait("Category", "Office")]
public class ExcelConverterTests(ExcelFixture fx) : IClassFixture<ExcelFixture>, IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    ConversionRouter Router => new([new ExcelConverter(fx.Excel)]);
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static readonly string[] Sources = ["xlsx", "xls", "ods", "csv", "tsv"];
    static readonly string[] Targets = ["xlsx", "ods", "csv", "tsv", "pdf"];

    public static TheoryData<string, string> SheetRoutes()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in Sources)
            foreach (var to in Targets.Where(t => t != from))
                data.Add(from, to);
        return data;
    }

    [Theory, MemberData(nameof(SheetRoutes))]
    public async Task Sheets_Convert(string from, string to)
    {
        var src = await fx.SourceAsync(from, Dir);
        var hash = SHA256.HashData(File.ReadAllBytes(src));

        var output = Assert.Single(await Router.ConvertAsync(src, to, Ctx));

        Assert.Equal(Path.Combine(Dir, $"가계부 (1).{to}"), output);
        switch (to)
        {
            case "csv" or "tsv":
                var bytes = File.ReadAllBytes(output);
                Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no BOM");
                var rows = new UTF8Encoding(false, true).GetString(bytes).Split("\r\n")
                    .Select(line => line.Split(to == "csv" ? ',' : '\t')).ToArray();
                Assert.Equal("이름", rows[0][0]); // the first sheet, not the active one
                Assert.Equal("42", rows[1][1]);
                break;
            case "pdf":
                var text = string.Join("\n", PdfText.PageTexts(output));
                Assert.Contains("이름", text);
                Assert.Contains("42", text);
                break;
            default:
                var (sheet, a1, b2) = await fx.ReadFirstSheetAsync(output);
                Assert.Equal("이름", a1);
                Assert.Equal(42d, b2);
                // Named after the source, not the private copy; Excel's ODS writer turns spaces in sheet names into "_".
                if (from is "csv" or "tsv") Assert.Equal(to == "ods" ? "가계부_(1)" : "가계부 (1)", sheet);
                break;
        }
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(src)));
        using (File.Open(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } // nothing holds the source
        Assert.Equal(new[] { Path.GetFileName(src), Path.GetFileName(output) }.Order(), Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName).Order());
    }

    public static TheoryData<string, string> TextEncodings() => new()
    {
        { "csv", "utf8-bom" },
        { "csv", "cp949" },    // Korean Excel's default "CSV (쉼표로 분리)"
        { "tsv", "utf16le" },  // Excel's own "유니코드 텍스트"
        { "tsv", "utf8-bom" },
    };

    [Theory, MemberData(nameof(TextEncodings))]
    public async Task TextSource_EncodingIsDetected(string ext, string encoding)
    {
        var sep = ext == "csv" ? "," : "\t";
        var content = $"이름{sep}\r\n{sep}42\r\n";
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = encoding switch
        {
            "utf8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)],
            "utf16le" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(content)],
            "cp949" => Encoding.GetEncoding(949).GetBytes(content),
            _ => throw new ArgumentException(encoding),
        };
        var src = Path.Combine(Dir, $"가계부.{ext}");
        File.WriteAllBytes(src, bytes);

        var output = Assert.Single(await Router.ConvertAsync(src, "xlsx", Ctx));

        var (_, a1, b2) = await fx.ReadFirstSheetAsync(output);
        Assert.Equal("이름", a1); // no mojibake, no U+FEFF
        Assert.Equal(42d, b2);
    }

    [Fact]
    public async Task OurExcel_ShowsNoWindow()
    {
        // An unactivated Excel shows its activation wizard (and then answers busy) as soon as Visible is written.
        using var host = new OfficeHost("Excel.Application", "EXCEL");
        await host.RunAsync(app => 0, TimeSpan.FromMinutes(1), CancellationToken.None);
        var p = host.LaunchedProcess!.Value;
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(0, OfficeProcesses.VisibleWindowCount(p));
            await Task.Delay(250);
        }
    }

    [Fact]
    public async Task PasswordProtected_ThrowsWithoutPrompt()
    {
        var src = await FixtureFactory.Sheet(fx.Excel, Dir, "잠김.xlsx", password: "secret");
        var sw = Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, "pdf", Ctx));
        Assert.Equal("암호로 보호된 문서는 열 수 없습니다.", e.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
        Assert.False(File.Exists(Path.Combine(Dir, "잠김.pdf")));
    }

    [Fact]
    public async Task CorruptFile_Throws()
    {
        var broken = await fx.SourceAsync("xlsx", Dir);
        var bytes = File.ReadAllBytes(broken);
        File.WriteAllBytes(broken, bytes[..(bytes.Length / 2)]);
        var e = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(broken, "pdf", Ctx));
        Assert.Equal("문서를 열 수 없습니다.", e.Message);
        Assert.Equal(new[] { "가계부 (1).xlsx" }, Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task MacroWorkbook_Refused()
    {
        var src = await FixtureFactory.MacroWorkbook(fx.Excel, Dir, "매크로.xls");
        if (src is null) Assert.Skip("VBA 프로젝트 접근이 꺼져 있어 매크로 픽스처를 만들 수 없음");
        var e = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, "xlsx", Ctx));
        Assert.Equal("매크로가 들어 있는 문서는 변환하지 않습니다.", e.Message);
    }

    [Fact]
    public async Task UserWorkbookInOurExcel_StaysOpen_AndExcelIsNotQuit()
    {
        // Stands in for a workbook the user double-clicked that Excel put into our hidden instance: the very file
        // we are asked to convert. Opening the user's path would hand back their workbook; Close would take it away.
        var src = await fx.SourceAsync("xlsx", Dir);
        OfficeProcess? ours = null;
        KillOnExitJob? job = null;
        try
        {
            using (var host = new OfficeHost("Excel.Application", "EXCEL"))
            {
                await host.RunAsync(app =>
                {
                    dynamic books = app.Workbooks;
                    Marshal.FinalReleaseComObject(books.Open(Filename: src));
                    Marshal.FinalReleaseComObject(books);
                    app.UserControl = true; // as for a file the user opened: Excel stays when automation lets go
                    return 0;
                }, TimeSpan.FromMinutes(1), CancellationToken.None);
                ours = host.LaunchedProcess;
                job = ours is { } o ? KillOnExitJob.TryCreate(o) : null; // test-owned leftover must not survive an aborted run

                Assert.Single(await new ConversionRouter([new ExcelConverter(host)]).ConvertAsync(src, "pdf", Ctx));

                var open = await host.RunAsync(app =>
                {
                    dynamic books = app.Workbooks;
                    try
                    {
                        var names = new List<string>();
                        foreach (dynamic b in books) { names.Add((string)b.FullName); Marshal.FinalReleaseComObject(b); }
                        return names;
                    }
                    finally { Marshal.FinalReleaseComObject(books); }
                }, TimeSpan.FromMinutes(1), CancellationToken.None);
                Assert.Equal(new[] { src }, open);
            }
            Assert.NotNull(ours);
            Assert.False(OfficeProcesses.WaitForExit(ours.Value, TimeSpan.FromSeconds(3)), "Excel holding the user's workbook was quit or killed");
        }
        finally
        {
            if (ours is { } p) OfficeProcesses.Kill(p); // the host left it running on purpose
            job?.Dispose();
        }
    }
}
