using UglyToad.PdfPig;
using Zest.Core.Catalog;
using Zest.Core.Jobs;
using Zest.Core.Metadata;
using Zest.Core.Tests.Fixtures;

namespace Zest.Core.Tests;

public class PdfMetadataTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static Task<IReadOnlyList<MetadataTag>> Read(string path) => MetadataReader.ReadAsync(path, Capabilities.All, CancellationToken.None);

    static readonly Dictionary<string, string> Info = new()
    {
        ["Title"] = "제주 일정", ["Author"] = "홍길동", ["Subject"] = "여행", ["Keywords"] = "제주, 여행", ["Creator"] = "Word",
    };

    string Fixture(int pages = 2) => FixtureFactory.Pdf(Dir, "일정.pdf", pages, info: Info);

    [Fact]
    public async Task Read_ListsTitleAndAuthor()
    {
        var tags = await Read(Fixture());

        var title = Assert.Single(tags, t => t.Id == "pdf:Title");
        Assert.Equal(("문서", "제목", "제주 일정", true), (title.Group, title.Label, title.Value, title.Editable));
        var created = Assert.Single(tags, t => t.Id == "pdf:CreationDate");
        Assert.False(created.Editable);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", created.Value);
        Assert.Equal("홍길동", Assert.Single(tags, t => t.Id == "pdf:Author").Value);
    }

    [Fact]
    public async Task Edit_Author_RoundTrips_AndProducerIsOurs()
    {
        var src = Fixture(3);
        var before = File.ReadAllBytes(src);
        var edit = new MetadataEdit(new Dictionary<string, string> { ["pdf:Author"] = "김제주", ["pdf:Producer"] = "Zest" },
            new HashSet<string>(), false, false);

        var output = await MetadataWriter.ApplyAsync(src, edit, Ctx);

        Assert.EndsWith(" (edited).pdf", output);
        var tags = await Read(output);
        Assert.Equal("김제주", Assert.Single(tags, t => t.Id == "pdf:Author").Value);
        Assert.Equal("Zest", Assert.Single(tags, t => t.Id == "pdf:Producer").Value);
        Assert.Equal("제주 일정", Assert.Single(tags, t => t.Id == "pdf:Title").Value);
        using (var pdf = PdfDocument.Open(output)) Assert.Equal(3, pdf.NumberOfPages);
        Assert.Equal(before, File.ReadAllBytes(src));
    }

    [Fact]
    public async Task RemoveAll_ClearsInfo()
    {
        var output = await MetadataWriter.ApplyAsync(Fixture(), MetadataEdit.Everything, Ctx);

        Assert.EndsWith(" (no metadata).pdf", output);
        var tags = await Read(output);
        foreach (var key in new[] { "Title", "Author", "Subject", "Keywords", "Creator", "Producer" })
            Assert.DoesNotContain(tags, t => t.Id == $"pdf:{key}");
        using (var pdf = PdfDocument.Open(output))
        {
            Assert.Equal(2, pdf.NumberOfPages);
            Assert.True(string.IsNullOrEmpty(pdf.Information.Producer));
            Assert.True(string.IsNullOrEmpty(pdf.Information.Creator));
            Assert.True(string.IsNullOrEmpty(pdf.Information.Title));
            Assert.False(pdf.TryGetXmpMetadata(out _));
        }
    }

    /// <summary>A one-page PDF whose Info dictionary body is <paramref name="info"/>; <paramref name="extra"/> are more objects (numbered from 6).</summary>
    string RawPdf(string info, params string[] extra)
    {
        var objects = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>", "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>", info, "<</Length 0>>\nstream\n\nendstream",
        };
        objects.AddRange(extra);
        var text = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(text.Length);
            text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = text.Length;
        text.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) text.Append($"{o:D10} 00000 n \n");
        text.Append($"trailer\n<</Size {objects.Count + 1}/Root 1 0 R/Info 4 0 R>>\nstartxref\n{xref}\n%%EOF\n");
        var path = Path.Combine(Dir, $"raw{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes(text.ToString()));
        return path;
    }

    static MetadataEdit SetAuthor(string value) =>
        new(new Dictionary<string, string> { ["pdf:Author"] = value }, new HashSet<string>(), false, false);

    [Fact]
    public async Task Read_ShowsDatesInLocalTime()
    {
        var src = RawPdf("<</CreationDate(D:20250101120000+09'00')>>");

        var created = Assert.Single(await Read(src), t => t.Id == "pdf:CreationDate");

        var expected = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.FromHours(9)).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        Assert.Equal(expected, created.Value);
    }

    [Fact]
    public async Task Edit_KeepsOriginalDates()
    {
        var src = RawPdf("<</CreationDate(D:20250101120000+09'00')/ModDate(D:20250301080000+09'00')/Author(a)>>");
        var before = await Read(src);

        var after = await Read(await MetadataWriter.ApplyAsync(src, SetAuthor("b"), Ctx));

        foreach (var key in new[] { "pdf:CreationDate", "pdf:ModDate" })
            Assert.Equal(Assert.Single(before, t => t.Id == key).Value, Assert.Single(after, t => t.Id == key).Value);
    }

    [Fact]
    public async Task RemoveAll_DropsCustomInfoKeys()
    {
        var src = RawPdf("<</Title(t)/Company(AcmeCorp)/Trapped/False>>");

        var output = await MetadataWriter.ApplyAsync(src, MetadataEdit.Everything, Ctx);

        using var pdf = PdfDocument.Open(output);
        Assert.False(pdf.Information.DocumentInformationDictionary!.TryGet(UglyToad.PdfPig.Tokens.NameToken.Create("Company"), out _));
        Assert.False(pdf.Information.DocumentInformationDictionary!.TryGet(UglyToad.PdfPig.Tokens.NameToken.Create("Title"), out _));
    }

    [Fact]
    public async Task Edit_KeepsIndirectValues()
    {
        var src = RawPdf("<</Title 6 0 R>>", "(Jeju)");

        var tags = await Read(await MetadataWriter.ApplyAsync(src, SetAuthor("b"), Ctx));

        Assert.Equal("Jeju", Assert.Single(tags, t => t.Id == "pdf:Title").Value);
    }

    [Fact]
    public async Task NoChangeSave_DoesNotReferencePdfSharpXmp()
    {
        var src = Fixture();

        var output = await MetadataWriter.ApplyAsync(src, SetAuthor("홍길동"), Ctx);

        using var pdf = PdfDocument.Open(output);
        if (pdf.TryGetXmpMetadata(out var xmp)) Assert.DoesNotContain("PDFsharp", xmp.GetXDocument()!.ToString());
        Assert.Equal("홍길동", Assert.Single(await Read(output), t => t.Id == "pdf:Author").Value);
    }

    [Fact]
    public async Task Encrypted_ThrowsKoreanError_LeavesNothing()
    {
        var src = FixtureFactory.Pdf(Dir, "잠금.pdf", 1, userPassword: "x");
        var edit = new MetadataEdit(new Dictionary<string, string> { ["pdf:Author"] = "a" }, new HashSet<string>(), false, false);

        var e = await Assert.ThrowsAsync<ZestException>(() => MetadataWriter.ApplyAsync(src, edit, Ctx));
        Assert.Equal("암호로 보호된 PDF는 바꿀 수 없습니다.", e.Message);
        Assert.Empty(Directory.GetFiles(Dir, "* (edited).pdf"));
    }
}
