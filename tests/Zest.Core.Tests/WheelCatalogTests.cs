using Zest.Core.Catalog;

namespace Zest.Core.Tests;

public class WheelCatalogTests
{
    static IReadOnlyList<WheelItem> Items(string[] paths, WheelMode mode) => WheelCatalog.Items(paths, mode, Capabilities.All);
    static string[] Labels(IEnumerable<WheelItem> items) => items.Select(i => i.Label).ToArray();

    [Fact] public void Convert_PngAndJpg_SharesCommonTargets() => Assert.Equal(new[] { "WEBP", "HEIC", "TIFF", "AVIF", "BMP", "PDF" }, Labels(Items(["a.png", "b.jpg"], WheelMode.Convert)));
    [Fact] public void Convert_PngAndPdf_HasJpgPngIntersectionOnly() => Assert.Equal(new[] { "JPG" }, Labels(Items(["a.png", "b.pdf"], WheelMode.Convert)));
    [Fact] public void Convert_UnknownExtension_Empty() => Assert.Empty(Items(["a.png", "b.xyz"], WheelMode.Convert));
    [Fact] public void Tools_OneImage_HasCrop() => Assert.Equal(new[] { "압축", "자르기", "PDF 만들기", "글자 복사", "배경 제거" }, Labels(Items(["a.png"], WheelMode.Tools)));
    [Fact] public void Tools_TwoImages_NoCrop() => Assert.DoesNotContain("자르기", Labels(Items(["a.png", "b.png"], WheelMode.Tools)));
    [Fact]
    public void Tools_HeicWithoutHeicWrite_HasNeitherCompressNorCrop()
    {
        var labels = Labels(WheelCatalog.Items(["a.heic"], WheelMode.Tools, Capabilities.All with { HeicWrite = false }));
        Assert.DoesNotContain("압축", labels);
        Assert.DoesNotContain("자르기", labels);
        Assert.Contains("PDF 만들기", labels);
    }

    [Fact] public void Tools_HeicWithHeicWrite_HasCompress() => Assert.Contains("압축", Labels(Items(["a.heic"], WheelMode.Tools)));
    [Fact] public void Tools_TwoPdfs_MergeFirst() => Assert.Equal(new[] { "합치기", "나누기", "글자 복사" }, Labels(Items(["a.pdf", "b.pdf"], WheelMode.Tools)));
    [Fact] public void Tools_ImageAndPdf_Empty() => Assert.Empty(Items(["a.png", "b.pdf"], WheelMode.Tools));
    [Fact] public void Tools_WithoutOcr_HidesCopyText() => Assert.DoesNotContain("글자 복사", Labels(WheelCatalog.Items(["a.png"], WheelMode.Tools, Capabilities.All with { Ocr = false })));
    [Fact] public void Actions_AlwaysFive() => Assert.Equal(new[] { "선반", "복사", "이동", "경로 복사", "터미널" }, Labels(Items(["a.xyz"], WheelMode.Actions)));

    [Fact]
    public void Folder_EmptiesConvertAndTools_KeepsActions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zest-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(dir, "사진.png");
        Directory.CreateDirectory(folder);
        try
        {
            string[] paths = [Path.Combine(dir, "a.png"), folder];
            Assert.Empty(Items(paths, WheelMode.Convert));
            Assert.Empty(Items(paths, WheelMode.Tools));
            Assert.Equal(5, Items(paths, WheelMode.Actions).Count);
        }
        finally { Directory.Delete(dir, true); }
    }
}
