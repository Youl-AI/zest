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
