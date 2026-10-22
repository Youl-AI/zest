namespace LimeWin.Core.Catalog;

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

        List<LimeTool> tools = [];
        if (formats.All(f => RasterImages.Contains(f!)))
        {
            tools.Add(LimeTool.Compress);
            if (paths.Count == 1) tools.Add(LimeTool.Crop);
            tools.Add(LimeTool.ImagesToPdf);
            if (caps.Ocr) tools.Add(LimeTool.CopyText);
            if (caps.BackgroundRemoval) tools.Add(LimeTool.RemoveBackground);
        }
        else if (formats.All(f => f == "pdf"))
        {
            if (paths.Count > 1) tools.Add(LimeTool.MergePdfs);
            tools.Add(LimeTool.SplitPdf);
            if (caps.Ocr) tools.Add(LimeTool.CopyText);
        }
        return tools.Select(t => new WheelItem(new ToolOp(t), ToolLabel(t))).ToList();
    }

    static IReadOnlyList<WheelItem> ActionItems() =>
        Enum.GetValues<DropAction>().Select(a => new WheelItem(new ActionOp(a), ActionLabel(a))).ToList();

    static string ToolLabel(LimeTool t) => t switch
    {
        LimeTool.Compress => "압축",
        LimeTool.Crop => "자르기",
        LimeTool.ImagesToPdf => "PDF 만들기",
        LimeTool.MergePdfs => "합치기",
        LimeTool.SplitPdf => "나누기",
        LimeTool.CopyText => "글자 복사",
        _ => "배경 제거",
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
