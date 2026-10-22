using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ImageMagick;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Office;
using LimeWin.Core.Pdf;
using LimeWin.Core.Tests.Fixtures;

namespace LimeWin.Core.Tests;

public class PowerPointConverterHandlesTests
{
    [Fact]
    public void Handles_SlideRoutes()
    {
        using var host = new OfficeHost("PowerPoint.Application", "POWERPNT"); // never started
        var c = new PowerPointConverter(host);
        foreach (var from in new[] { "ppt", "pptx", "odp" })
            foreach (var to in new[] { "pdf", "jpg", "png" })
                Assert.True(c.Handles(from, to), $"{from}->{to}");
        Assert.True(c.Handles("ppt", "pptx"));
        Assert.False(c.Handles("odp", "pptx"));  // pptx is only offered for ppt
        Assert.False(c.Handles("pptx", "pptx"));
        Assert.False(c.Handles("pptx", "ppt"));  // input only
        Assert.False(c.Handles("docx", "pdf"));
    }
}

/// <summary>
/// One PowerPoint host for the class. It idles out after 2 s, so the tests that need a PowerPoint of their own (the
/// "user's") can wait for ours to quit: PowerPoint is single-instance.
/// </summary>
public sealed class PowerPointFixture : IDisposable
{
    public OfficeHost PowerPoint { get; } = new("PowerPoint.Application", "POWERPNT") { IdleDelay = TimeSpan.FromSeconds(2) };
    readonly string _cache = TestFiles.TempDir();
    readonly Dictionary<string, string> _masters = [];

    /// <summary>A fresh copy of the 3-slide fixture in <paramref name="format"/>, named <c>발표 (1).format</c> in <paramref name="dir"/>.</summary>
    public async Task<string> SourceAsync(string format, string dir)
    {
        if (!_masters.TryGetValue(format, out var master))
            _masters[format] = master = await FixtureFactory.Slides(PowerPoint, _cache, $"master.{format}");
        var copy = Path.Combine(dir, $"발표 (1).{format}");
        File.Copy(master, copy);
        return copy;
    }

    public void Dispose()
    {
        var launched = PowerPoint.LaunchedProcess;
        PowerPoint.Dispose();
        try { Directory.Delete(_cache, true); } catch (IOException) { }
        if (launched is { } p && OfficeProcesses.IsAlive(p))
            throw new InvalidOperationException($"POWERPNT PID {p.Id} started by the test host is still running after Dispose.");
    }
}

/// <summary>
/// The test's stand-in for a PowerPoint the user has open: started here (after any PowerPoint of ours has quit),
/// hidden, in a kill-on-close job so an aborted run cannot orphan it. Skips when a PowerPoint not started by this
/// run is open, because then that is the user's and tests must not touch it.
/// </summary>
sealed class UserPowerPoint : IDisposable
{
    public required dynamic App { get; init; }
    public required OfficeProcess Process { get; init; }
    KillOnExitJob? _job;

    public static async Task<UserPowerPoint> StartAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (OfficeProcesses.Snapshot("POWERPNT").Count > 0)
        {
            if (DateTime.UtcNow > deadline) Assert.Skip("PowerPoint가 이미 실행 중이라 사용자 PowerPoint 시험을 할 수 없음");
            await Task.Delay(250);
        }
        var before = OfficeProcesses.Snapshot("POWERPNT");
        dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
        var launched = OfficeProcesses.Launched(before, OfficeProcesses.Snapshot("POWERPNT"));
        if (launched is null)
        {
            Marshal.FinalReleaseComObject(app);
            throw new InvalidOperationException("the test's PowerPoint did not start as a new process");
        }
        return new UserPowerPoint { App = app, Process = launched.Value, _job = KillOnExitJob.TryCreate(launched.Value) };
    }

    public int PresentationCount()
    {
        dynamic list = App.Presentations;
        try { return (int)list.Count; }
        finally { Marshal.FinalReleaseComObject(list); }
    }

    public void Dispose()
    {
        try
        {
            dynamic list = App.Presentations;
            foreach (dynamic p in list) { p.Close(); Marshal.FinalReleaseComObject(p); }
            Marshal.FinalReleaseComObject(list);
            App.Quit();
        }
        catch (COMException) { } // the kill below closes it
        Marshal.FinalReleaseComObject(App);
        if (!OfficeProcesses.WaitForExit(Process, TimeSpan.FromSeconds(10))) OfficeProcesses.Kill(Process);
        _job?.Dispose();
    }
}

[Trait("Category", "Office")]
public class PowerPointConverterTests(PowerPointFixture fx) : IClassFixture<PowerPointFixture>, IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    ConversionRouter Router => new([new PowerPointConverter(fx.PowerPoint)]);
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    public static TheoryData<string, string> SlideRoutes()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in new[] { "ppt", "pptx", "odp" })
            foreach (var to in new[] { "pdf", "jpg", "png" })
                data.Add(from, to);
        data.Add("ppt", "pptx");
        return data;
    }

    [Fact]
    public async Task PasswordSyntax_WorksOnPlainFile()
    {
        var src = await fx.SourceAsync("pptx", Dir);
        var slides = await fx.PowerPoint.RunAsync(app =>
        {
            dynamic list = app.Presentations;
            dynamic pres = list.Open(FileName: src + PowerPointConst.WrongPasswordSuffix, ReadOnly: MsoConst.True,
                Untitled: MsoConst.False, WithWindow: MsoConst.False);
            try { dynamic s = pres.Slides; int n = s.Count; Marshal.FinalReleaseComObject(s); return n; }
            finally
            {
                pres.Close();
                Marshal.FinalReleaseComObject(pres);
                Marshal.FinalReleaseComObject(list);
            }
        }, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(3, slides);
    }

    [Theory, MemberData(nameof(SlideRoutes))]
    public async Task Slides_Convert(string from, string to)
    {
        var src = await fx.SourceAsync(from, Dir);
        var hash = SHA256.HashData(File.ReadAllBytes(src));

        var output = Assert.Single(await Router.ConvertAsync(src, to, Ctx));

        switch (to)
        {
            case "pdf":
                Assert.Equal(Path.Combine(Dir, "발표 (1).pdf"), output);
                Assert.Equal(3, await PdfRenderer.PageCountAsync(output));
                Assert.Contains("슬라이드 2", string.Join("\n", PdfText.PageTexts(output)));
                break;
            case "pptx":
                Assert.Equal(Path.Combine(Dir, "발표 (1).pptx"), output);
                using (var zip = ZipFile.OpenRead(output))
                    Assert.Equal(3, zip.Entries.Count(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal)));
                break;
            default:
                Assert.Equal(Path.Combine(Dir, "발표 (1) (pages)"), output);
                var files = Directory.GetFiles(output).Select(Path.GetFileName).Order().ToArray();
                Assert.Equal(new[] { $"발표 (1) 1.{to}", $"발표 (1) 2.{to}", $"발표 (1) 3.{to}" }, files);
                foreach (var f in Directory.GetFiles(output))
                {
                    var info = new MagickImageInfo(f);
                    Assert.Equal(1920u, info.Width);
                    Assert.Equal(1080u, info.Height); // 16:9 fixture
                    Assert.Equal(to == "jpg" ? MagickFormat.Jpeg : MagickFormat.Png, info.Format);
                }
                break;
        }
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(src)));
        using (File.Open(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } // nothing holds the source
        Assert.Equal(new[] { Path.GetFileName(src), Path.GetFileName(output) }.Order(), Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task SingleSlide_ToPng_IsOneFile()
    {
        var src = await FixtureFactory.Slides(fx.PowerPoint, Dir, "한 장.pptx", slides: 1);
        var output = Assert.Single(await Router.ConvertAsync(src, "png", Ctx));
        Assert.Equal(Path.Combine(Dir, "한 장.png"), output);
        Assert.Equal(1920u, new MagickImageInfo(output).Width);
    }

    [Fact]
    public async Task PasswordProtected_ThrowsWithoutPrompt()
    {
        foreach (var name in new[] { "잠김.pptx", "잠김.ppt" })
        {
            var src = await FixtureFactory.Slides(fx.PowerPoint, Dir, name, slides: 1, password: "secret");
            var sw = Stopwatch.StartNew();
            var e = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, "pdf", Ctx));
            Assert.Equal("암호로 보호된 문서는 열 수 없습니다.", e.Message);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
            Assert.False(File.Exists(Path.Combine(Dir, "잠김.pdf")));
        }
    }

    [Fact]
    public async Task CorruptFile_Throws()
    {
        var broken = await fx.SourceAsync("pptx", Dir);
        var bytes = File.ReadAllBytes(broken);
        File.WriteAllBytes(broken, bytes[..(bytes.Length / 2)]);
        var e = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(broken, "pdf", Ctx));
        Assert.Equal("문서를 열 수 없습니다.", e.Message);
        Assert.Equal(new[] { "발표 (1).pptx" }, Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task MacroPresentation_Refused()
    {
        var src = await FixtureFactory.MacroPresentation(fx.PowerPoint, Dir, "매크로.ppt");
        if (src is null) Assert.Skip("VBA 프로젝트 접근이 꺼져 있어 매크로 픽스처를 만들 수 없음");
        var e = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, "pdf", Ctx));
        Assert.Equal("매크로가 들어 있는 문서는 변환하지 않습니다.", e.Message);
    }

    [Fact]
    public async Task UserPowerPointOpen_IsNotQuit()
    {
        var src = await fx.SourceAsync("pptx", Dir); // before the user's PowerPoint: may start (then idle out) ours
        var userDir = TestFiles.TempDir();
        try
        {
            using var user = await UserPowerPoint.StartAsync();
            dynamic list = user.App.Presentations;
            dynamic userPres = list.Add(WithWindow: MsoConst.False);
            Marshal.FinalReleaseComObject(userPres.Slides.Add(1, 11));
            userPres.SaveAs(Path.Combine(userDir, "사용자 발표.pptx"), PowerPointFormat.Pptx);
            Marshal.FinalReleaseComObject(userPres);
            Marshal.FinalReleaseComObject(list);

            using (var host = new OfficeHost("PowerPoint.Application", "POWERPNT"))
            {
                Assert.Single(await new ConversionRouter([new PowerPointConverter(host)]).ConvertAsync(src, "png", Ctx));
                Assert.Null(host.LaunchedProcess); // attached: the instance is the user's
            }

            Assert.True(OfficeProcesses.IsAlive(user.Process), "the user's PowerPoint was closed");
            Assert.Equal(1, user.PresentationCount());
        }
        finally { try { Directory.Delete(userDir, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task AttachedTimeout_AbandonsStuckThread_NextJobRuns()
    {
        // A call into the user's PowerPoint that hangs cannot be ended by killing it (it is not ours). The host must
        // give that STA thread up so later jobs do not queue behind it forever.
        using var user = await UserPowerPoint.StartAsync();
        using var host = new OfficeHost("PowerPoint.Application", "POWERPNT");
        await host.RunAsync(app => 0, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Null(host.LaunchedProcess);

        var e = await Assert.ThrowsAsync<LimeException>(() =>
            host.RunAsync<int>(app => { Thread.Sleep(8000); return 0; }, TimeSpan.FromMilliseconds(300), CancellationToken.None));
        Assert.Equal("Office 응답이 없어 중단했습니다.", e.Message);

        var sw = Stopwatch.StartNew();
        var count = await host.RunAsync(app => { dynamic l = app.Presentations; int n = l.Count; Marshal.FinalReleaseComObject(l); return n; },
            TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(0, count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"next job waited {sw.Elapsed} behind the stuck one");
        Assert.True(OfficeProcesses.IsAlive(user.Process), "the user's PowerPoint was killed");
    }
}
