using Zest.Core.Catalog;

namespace Zest.Core.Tests;

public class FormatCatalogTests
{
    [Theory]
    [InlineData(@"C:\a\photo.JPEG", "jpg")] [InlineData(@"C:\a\x.tif", "tiff")] [InlineData(@"C:\a\x.Heif", "heic")]
    [InlineData(@"C:\a\노트.markdown", "md")] [InlineData(@"C:\a\noext", null)]
    public void FormatOf_NormalizesAliases(string path, string? expected) => Assert.Equal(expected, FormatCatalog.FormatOf(path));

    [Fact]
    public void FormatOf_ExistingDirectory_IsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "limewin-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(dir, "사진.png");
        Directory.CreateDirectory(folder);
        try { Assert.Null(FormatCatalog.FormatOf(folder)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact] public void TargetsFor_Png_IsImageTargetsWithoutPng() =>
        Assert.Equal(new[] { "jpg", "webp", "heic", "tiff", "avif", "bmp", "pdf" }, FormatCatalog.TargetsFor("png", Capabilities.All));
    [Fact] public void TargetsFor_Svg_IsInputOnly_NotATarget() => Assert.DoesNotContain("svg", FormatCatalog.TargetsFor("png", Capabilities.All));
    [Fact] public void TargetsFor_Webm_ExcludesWebm_IncludesGifMp3() { var t = FormatCatalog.TargetsFor("webm", Capabilities.All); Assert.DoesNotContain("webm", t); Assert.Contains("gif", t); Assert.Contains("mp3", t); }
    // A hidden Word never finishes opening a PDF (Task 8), so pdf -> docx is never offered, with or without Office.
    [Theory, InlineData(true), InlineData(false)]
    public void TargetsFor_Pdf_NeverOffersDocx(bool office) =>
        Assert.Equal(new[] { "jpg", "png", "txt" }, FormatCatalog.TargetsFor("pdf", Capabilities.All with { Office = office }));
    [Fact] public void TargetsFor_Png_WithoutHeicWrite_DropsHeic() => Assert.DoesNotContain("heic", FormatCatalog.TargetsFor("png", Capabilities.All with { HeicWrite = false }));
    [Fact] public void TargetsFor_Ppt_IncludesPptx_ButPptxDoesNot() { Assert.Contains("pptx", FormatCatalog.TargetsFor("ppt", Capabilities.All)); Assert.DoesNotContain("pptx", FormatCatalog.TargetsFor("pptx", Capabilities.All)); }
    [Fact] public void TargetsFor_Md_WithoutOffice_KeepsHtmlOnly() => Assert.Equal(new[] { "html" }, FormatCatalog.TargetsFor("md", Capabilities.All with { Office = false }));
}
