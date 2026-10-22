using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Office;
using LimeWin.Core.Pdf;
using LimeWin.Core.Tests.Fixtures;

namespace LimeWin.Core.Tests;

public class WordConverterHandlesTests
{
    [Fact]
    public void Handles_DocumentRoutes_ButNotPdf()
    {
        using var host = new OfficeHost("Word.Application", "WINWORD"); // never started: Handles does not touch Word
        var c = new WordConverter(host);
        Assert.True(c.Handles("docx", "pdf"));
        Assert.True(c.Handles("doc", "docx"));
        Assert.True(c.Handles("md", "docx"));
        Assert.True(c.Handles("html", "md"));
        Assert.False(c.Handles("pdf", "docx")); // hidden Word never finishes opening a PDF (Task 8)
        Assert.False(c.Handles("pdf", "txt"));  // PdfConverter's route
        Assert.False(c.Handles("docx", "docx"));
        Assert.False(c.Handles("docx", "doc")); // input only
        Assert.False(c.Handles("png", "docx"));
    }

    [Fact]
    public async Task MarkdownToHtml_AndBack_DoNotStartWord()
    {
        var dir = TestFiles.TempDir();
        try
        {
            using var host = new OfficeHost("Word.Application", "WINWORD");
            var router = new ConversionRouter([new WordConverter(host)]);
            var md = Path.Combine(dir, "노트.md");
            File.WriteAllText(md, "# 제목\n\n**굵게**\n");

            var html = Assert.Single(await router.ConvertAsync(md, "html", new OperationContext(CancellationToken.None)));
            Assert.Equal(Path.Combine(dir, "노트.html"), html);
            Assert.Contains("<h1", File.ReadAllText(html));
            Assert.Contains("<meta charset=\"utf-8\">", File.ReadAllText(html));

            File.Delete(md);
            var back = Assert.Single(await router.ConvertAsync(html, "md", new OperationContext(CancellationToken.None)));
            Assert.Contains("# 제목", File.ReadAllText(back));

            Assert.Null(host.LaunchedProcess);
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>One Word for the whole class; its disposal must leave no WINWORD.EXE we started.</summary>
public sealed class WordFixture : IDisposable
{
    public OfficeHost Word { get; } = new("Word.Application", "WINWORD");
    readonly string _cache = TestFiles.TempDir();
    readonly Dictionary<string, string> _masters = [];

    /// <summary>A fresh copy of the "안녕 Hello" fixture in <paramref name="format"/>, named <c>문서 (1).format</c> in <paramref name="dir"/>.</summary>
    public async Task<string> SourceAsync(string format, string dir)
    {
        if (!_masters.TryGetValue(format, out var master))
            _masters[format] = master = await FixtureFactory.Document(Word, _cache, $"master.{format}");
        var copy = Path.Combine(dir, $"문서 (1).{format}");
        File.Copy(master, copy);
        return copy;
    }

    public void Dispose()
    {
        var launched = Word.LaunchedProcess;
        Word.Dispose();
        try { Directory.Delete(_cache, true); } catch (IOException) { }
        if (launched is { } p && OfficeProcesses.IsAlive(p))
            throw new InvalidOperationException($"WINWORD PID {p.Id} started by the test host is still running after Dispose.");
    }
}

[Trait("Category", "Office")]
public class WordConverterTests(WordFixture fx) : IClassFixture<WordFixture>, IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    ConversionRouter Router => new([new WordConverter(fx.Word)]);
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static readonly string[] Sources = ["docx", "doc", "rtf", "odt", "txt", "html", "md"];
    static readonly string[] Targets = ["docx", "odt", "txt", "md", "html", "pdf"];

    public static TheoryData<string, string> WordRoutes()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in Sources)
            foreach (var to in Targets.Where(t => t != from))
                data.Add(from, to);
        return data;
    }

    static string ZipXmlText(string path, string entry)
    {
        using var zip = ZipFile.OpenRead(path);
        using var s = (zip.GetEntry(entry) ?? throw new InvalidOperationException($"{entry} missing in {path}")).Open();
        return XDocument.Load(s).Root!.Value; // all text nodes, so split runs still read as one string
    }

    static string ResultText(string path, string to) => to switch
    {
        "pdf" => string.Join("\n", PdfText.PageTexts(path)),
        "docx" => ZipXmlText(path, "word/document.xml"),
        "odt" => ZipXmlText(path, "content.xml"),
        _ => File.ReadAllText(path, System.Text.Encoding.UTF8),
    };

    [Theory, MemberData(nameof(WordRoutes))]
    public async Task Converts_AndResultOpens(string from, string to)
    {
        var src = await fx.SourceAsync(from, Dir);
        var hash = SHA256.HashData(File.ReadAllBytes(src));

        var output = Assert.Single(await Router.ConvertAsync(src, to, Ctx));

        Assert.Equal(Path.Combine(Dir, $"문서 (1).{to}"), output);
        Assert.Contains("안녕", ResultText(output, to));
        if (to is "txt" or "md" or "html")
        {
            var b = File.ReadAllBytes(output);
            Assert.False(b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF, "no BOM");
        }
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(src)));
        Assert.Equal(new[] { Path.GetFileName(src), Path.GetFileName(output) }.Order(), Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task PasswordProtected_ThrowsWithoutPrompt()
    {
        var src = await FixtureFactory.Document(fx.Word, Dir, "잠김.docx", password: "secret");
        var sw = Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, "pdf", Ctx));
        Assert.Equal("암호로 보호된 문서는 열 수 없습니다.", e.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
        Assert.False(File.Exists(Path.Combine(Dir, "잠김.pdf")));
    }

    [Fact]
    public async Task MacroDoc_Refused()
    {
        var src = await FixtureFactory.MacroDocument(fx.Word, Dir, "매크로.doc");
        if (src is null) Assert.Skip("VBA 프로젝트 접근이 꺼져 있어 매크로 픽스처를 만들 수 없음");
        var e = await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, "docx", Ctx));
        Assert.Equal("매크로가 들어 있는 문서는 변환하지 않습니다.", e.Message);
        Assert.False(File.Exists(Path.Combine(Dir, "매크로.docx")));
    }

    [Fact]
    public async Task CorruptDocx_Throws_AndNoWordLeftRunning()
    {
        var empty = TestFiles.Touch(Dir, "빈.docx");
        var broken = await fx.SourceAsync("docx", Dir);
        var bytes = File.ReadAllBytes(broken);
        File.WriteAllBytes(broken, bytes[..(bytes.Length / 2)]); // a docx cut off half way
        OfficeProcess? launched;
        using (var host = new OfficeHost("Word.Application", "WINWORD"))
        {
            var router = new ConversionRouter([new WordConverter(host)]);
            var e = await Assert.ThrowsAsync<LimeException>(() => router.ConvertAsync(empty, "pdf", Ctx));
            Assert.Equal("빈 파일은 변환할 수 없습니다.", e.Message);
            var sw = Stopwatch.StartNew();
            e = await Assert.ThrowsAsync<LimeException>(() => router.ConvertAsync(broken, "pdf", Ctx));
            Assert.Equal("문서를 열 수 없습니다.", e.Message);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
            launched = host.LaunchedProcess;
        }
        Assert.NotNull(launched);
        Assert.False(OfficeProcesses.IsAlive(launched.Value), $"WINWORD PID {launched.Value.Id} still running");
        Assert.Equal(new[] { "문서 (1).docx", "빈.docx" }, Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Timeout_KillsOnlyOurWord_AndHostRecovers()
    {
        using var host = new OfficeHost("Word.Application", "WINWORD");
        await host.RunAsync(app => 0, TimeSpan.FromMinutes(1), CancellationToken.None);
        var first = host.LaunchedProcess!.Value;

        var e = await Assert.ThrowsAsync<LimeException>(() =>
            host.RunAsync<int>(app => { Thread.Sleep(3000); return 0; }, TimeSpan.FromMilliseconds(300), CancellationToken.None));
        Assert.Equal("Office 응답이 없어 중단했습니다.", e.Message);
        Assert.True(OfficeProcesses.WaitForExit(first, TimeSpan.FromSeconds(10)), "timed-out Word was not killed");

        int count = await host.RunAsync(app => { dynamic docs = app.Documents; int n = docs.Count; Marshal.FinalReleaseComObject(docs); return n; },
            TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(0, count);
        Assert.NotEqual(first, host.LaunchedProcess);
    }

    [Fact]
    public async Task Idle_QuitsOurWord_WhenNoDocumentIsOpen()
    {
        using var host = new OfficeHost("Word.Application", "WINWORD") { IdleDelay = TimeSpan.FromSeconds(1) };
        await host.RunAsync(app => 0, TimeSpan.FromMinutes(1), CancellationToken.None);
        var p = host.LaunchedProcess!.Value;
        Assert.True(OfficeProcesses.WaitForExit(p, TimeSpan.FromSeconds(20)), "idle Word did not quit");
    }

    [Fact]
    public async Task Idle_WithDocumentStillOpen_OnlyDisconnects()
    {
        OfficeProcess? p = null;
        KillOnExitJob? leftoverJob = null;
        try
        {
            using var host = new OfficeHost("Word.Application", "WINWORD") { IdleDelay = TimeSpan.FromSeconds(1) };
            await host.RunAsync(app =>
            {
                dynamic docs = app.Documents;
                Marshal.FinalReleaseComObject(docs.Add()); // stands in for a document that is not ours
                Marshal.FinalReleaseComObject(docs);
                return 0;
            }, TimeSpan.FromMinutes(1), CancellationToken.None);
            p = host.LaunchedProcess!.Value;
            Assert.False(OfficeProcesses.WaitForExit(p.Value, TimeSpan.FromSeconds(4)), "Word with an open document was quit");
            leftoverJob = KillOnExitJob.TryCreate(p.Value); // the host let it go on purpose; an aborted run must not orphan it
        }
        finally
        {
            if (p is { } left) OfficeProcesses.Kill(left); // test-owned leftover
            leftoverJob?.Dispose();
        }
    }

    /// <summary>
    /// Calls into the test's own "user" Word from this MTA thread, which has no message filter: retries while Word
    /// answers busy (RPC_E_SERVERCALL_RETRYLATER / RPC_E_CALL_REJECTED).
    /// </summary>
    static T WhenNotBusy<T>(Func<T> call, int seconds = 30)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (true)
        {
            try { return call(); }
            catch (COMException e) when ((uint)e.HResult is 0x8001010A or 0x80010001 && DateTime.UtcNow < deadline) { Thread.Sleep(100); }
        }
    }

    [Fact]
    public async Task UserOpenDocument_IsNeverClosed()
    {
        // The "user's" Word: started here with its own temp document open, then shown (minimized).
        // It is made visible last: an unactivated Office shows a modal activation wizard in any visible Word, after
        // which that Word answers every call with "busy". So the checks below read the document's owner file
        // ("~$..."), which Word keeps exactly while the document is open, instead of calling into that Word.
        var userDir = TestFiles.TempDir();
        var userDocPath = Path.Combine(userDir, "사용자 문서.docx");
        var before = OfficeProcesses.Snapshot("WINWORD");
        dynamic userApp = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
        var userPid = OfficeProcesses.Launched(before, OfficeProcesses.Snapshot("WINWORD"));
        using var userJob = userPid is { } u ? KillOnExitJob.TryCreate(u) : null; // dies with an aborted run
        dynamic? userDocs = null, userDoc = null;
        try
        {
            Assert.NotNull(userPid);
            userDocs = WhenNotBusy(() => userApp.Documents);
            userDoc = WhenNotBusy(() => userDocs!.Add());
            WhenNotBusy(() => userDoc!.SaveAs2(FileName: userDocPath, FileFormat: WordFormat.Docx));
            WhenNotBusy(() => userApp.WindowState = 2); // wdWindowStateMinimize
            WhenNotBusy(() => userApp.Visible = true);
            Assert.NotEmpty(Directory.GetFiles(userDir, "~$*"));

            var src = await fx.SourceAsync("docx", Dir);
            using (var host = new OfficeHost("Word.Application", "WINWORD"))
            {
                Assert.Single(await new ConversionRouter([new WordConverter(host)]).ConvertAsync(src, "pdf", Ctx));
                Assert.NotEqual(userPid, host.LaunchedProcess); // never claims the user's Word as its own
            }

            Assert.True(OfficeProcesses.IsAlive(userPid.Value), "the user's Word was closed");
            Assert.NotEmpty(Directory.GetFiles(userDir, "~$*")); // the user's document is still open
        }
        finally
        {
            try
            {
                if (userDoc is not null)
                {
                    WhenNotBusy(() => userDoc.Close(SaveChanges: WordConst.DoNotSaveChanges), seconds: 5);
                    Marshal.FinalReleaseComObject(userDoc);
                }
                if (userDocs is not null) Marshal.FinalReleaseComObject(userDocs);
                WhenNotBusy(() => userApp.Quit(SaveChanges: WordConst.DoNotSaveChanges), seconds: 5);
            }
            catch (COMException) { } // still busy behind the activation wizard: the kill below closes it
            Marshal.FinalReleaseComObject(userApp);
            if (userPid is { } up && !OfficeProcesses.WaitForExit(up, TimeSpan.FromSeconds(10))) OfficeProcesses.Kill(up);
            try { Directory.Delete(userDir, true); } catch (IOException) { }
        }
    }
}
