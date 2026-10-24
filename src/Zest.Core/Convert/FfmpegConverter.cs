using System.Globalization;
using Zest.Core.Catalog;
using Zest.Core.Jobs;
using Zest.Core.Output;

namespace Zest.Core.Convert;

/// <summary>Audio, video and GIF conversion through ffmpeg.</summary>
public sealed class FfmpegConverter(string ffmpegPath) : IConverter
{
    readonly string? _ffprobePath = FfmpegLocator.LocateProbe(ffmpegPath);

    public bool Handles(string from, string to)
    {
        if (from == to || !FfmpegArgs.IsTarget(to)) return false;
        var toCategory = FormatCatalog.CategoryOf(to);
        return FormatCatalog.CategoryOf(from) switch
        {
            FormatCategory.Audio => toCategory == FormatCategory.Audio,
            FormatCategory.Gif => toCategory == FormatCategory.Video,
            FormatCategory.Video => toCategory == FormatCategory.Video || to is "gif" or "mp3",
            _ => false,
        };
    }

    public Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx) =>
        ctx.InSlotAsync(EngineGates.Ffmpeg, () => ConvertInSlotAsync(source, to, ctx));

    async Task<IReadOnlyList<string>> ConvertInSlotAsync(string source, string to, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var from = FormatCatalog.FormatOf(source) ?? "";
        var duration = await ProbeDurationAsync(source, ctx.Token);

        var output = OutputNaming.ConvertOutput(source, to, ctx); // ffmpeg writes over our placeholder (-y); the router removes it on failure or cancel
        await FfmpegProcess.RunAsync(ffmpegPath, ["-progress", "pipe:1", "-nostats", .. FfmpegArgs.For(source, from, to, output)], duration, ctx);
        ctx.Report(1);
        return [output];
    }

    async Task<double?> ProbeDurationAsync(string source, CancellationToken token)
    {
        if (_ffprobePath is null) return null;
        var text = await FfmpegProcess.CaptureAsync(_ffprobePath,
            ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", source], token);
        return double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : null;
    }
}
