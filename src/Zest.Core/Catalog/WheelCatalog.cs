using Zest.Core.Metadata;

namespace Zest.Core.Catalog;

public static class WheelCatalog
{
    static readonly HashSet<string> RasterImages = ["jpg", "png", "webp", "heic", "tiff", "avif", "bmp"];

    public static IReadOnlyList<WheelItem> Items(IReadOnlyList<string> paths, WheelMode mode, Capabilities caps) => mode switch
    {
        WheelMode.Convert => ConvertItems(paths, caps),
        WheelMode.Tools => ToolItems(paths, caps),
        _ => ActionItems(),
    };

    static IReadOnlyList<WheelItem> ConvertItems(IReadOnlyList<string> paths, Capabilities caps)
    {
        if (paths.Count == 0) return [];
        IEnumerable<string>? common = null;
        foreach (var path in paths)
        {
            var format = FormatCatalog.FormatOf(path);
            if (format is null) return [];
            var targets = FormatCatalog.TargetsFor(format, caps);
            if (targets.Count == 0) return [];
            common = common is null ? targets : common.Intersect(targets);
        }
        return common!.Select(t => new WheelItem(new ConvertOp(t), t.ToUpperInvariant())).ToList();
    }

    static IReadOnlyList<WheelItem> ToolItems(IReadOnlyList<string> paths, Capabilities caps)
    {
        if (paths.Count == 0) return [];
        var formats = paths.Select(FormatCatalog.FormatOf).ToList();
        if (formats.Any(f => f is null)) return [];

        List<ZestTool> tools = [];
        if (formats.All(f => RasterImages.Contains(f!)))
        {
            var writable = formats.All(f => CanWriteOwnFormat(f!, caps));
            if (writable) tools.Add(ZestTool.Compress);
            if (writable && paths.Count == 1) tools.Add(ZestTool.Crop);
            tools.Add(ZestTool.ImagesToPdf);
            if (caps.Ocr) tools.Add(ZestTool.CopyText);
            if (caps.BackgroundRemoval) tools.Add(ZestTool.RemoveBackground);
            if (formats.All(f => MetadataFormats.Supports(f!, caps))) tools.Add(ZestTool.Metadata);
        }
        else if (formats.All(f => f == "pdf"))
        {
            if (paths.Count > 1) tools.Add(ZestTool.MergePdfs);
            tools.Add(ZestTool.SplitPdf);
            if (caps.Ocr) tools.Add(ZestTool.CopyText);
            tools.Add(ZestTool.Metadata);
        }
        else if (formats.All(f => FormatCatalog.CategoryOf(f!) is FormatCategory.Audio or FormatCategory.Video)
                 && caps.Ffmpeg && caps.Ffprobe)
        {
            tools.Add(ZestTool.Metadata);
        }
        return tools.Select(t => new WheelItem(new ToolOp(t), ToolLabel(t))).ToList();
    }

    /// <summary>Compress and Crop write the source's own format, so a format Magick cannot encode here gets neither petal.</summary>
    static bool CanWriteOwnFormat(string format, Capabilities caps) => format switch
    {
        "heic" => caps.HeicWrite,
        "avif" => caps.AvifWrite,
        _ => true,
    };

    static IReadOnlyList<WheelItem> ActionItems() =>
        Enum.GetValues<DropAction>().Select(a => new WheelItem(new ActionOp(a), ActionLabel(a))).ToList();

    static string ToolLabel(ZestTool t) => t switch
    {
        ZestTool.Compress => "압축",
        ZestTool.Crop => "자르기",
        ZestTool.ImagesToPdf => "PDF 만들기",
        ZestTool.MergePdfs => "합치기",
        ZestTool.SplitPdf => "나누기",
        ZestTool.CopyText => "글자 복사",
        ZestTool.RemoveBackground => "배경 제거",
        ZestTool.Metadata => "메타데이터",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    static string ActionLabel(DropAction a) => a switch
    {
        DropAction.Shelf => "선반",
        DropAction.Copy => "복사",
        DropAction.Move => "이동",
        DropAction.CopyPath => "경로 복사",
        _ => "터미널",
    };
}
