using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using LimeWin.Core.Catalog;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;

namespace LimeWin.Core.Convert;

/// <summary>Audio, video and GIF conversion through ffmpeg.</summary>
public sealed class FfmpegConverter(string ffmpegPath) : IConverter
{
    const int StderrLines = 20;

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
        await RunAsync(["-progress", "pipe:1", "-nostats", .. FfmpegArgs.For(source, from, to, output)], duration, ctx);
        ctx.Report(1);
        return [output];
    }

    async Task RunAsync(IReadOnlyList<string> args, double? duration, OperationContext ctx)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception) { throw new LimeException("변환 실패: ffmpeg를 실행할 수 없습니다."); }
        using var tiedToUs = Office.KillOnExitJob.TryCreate(process); // if Lime dies, ffmpeg goes with it

        // Cancel kills the whole tree; we then wait for exit so the output file is unlocked before the router deletes it.
        using var killOnCancel = ctx.Token.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });

        var tail = new ConcurrentQueue<string>();
        var stderr = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) is not null)
            {
                if (line.Length == 0) continue;
                tail.Enqueue(line);
                while (tail.Count > StderrLines) tail.TryDequeue(out _);
            }
        });

        try
        {
            string? progressLine;
            while ((progressLine = await process.StandardOutput.ReadLineAsync()) is not null)
            {
                if (duration is > 0 && progressLine.StartsWith("out_time_us=", StringComparison.Ordinal)
                    && long.TryParse(progressLine.AsSpan("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
                    ctx.Report(us / 1_000_000.0 / duration.Value);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await stderr;
        }
        finally
        {
            // If anything above threw, ffmpeg may still be running and holding the output file.
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(bound.Token); }
                catch (OperationCanceledException) { }
            }
        }

        ctx.Token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new LimeException($"변환 실패: {tail.LastOrDefault() ?? $"종료 코드 {process.ExitCode}"}");
    }

    async Task<double?> ProbeDurationAsync(string source, CancellationToken token)
    {
        if (_ffprobePath is null) return null;
        var psi = new ProcessStartInfo(_ffprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", source })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            using var tiedToUs = Office.KillOnExitJob.TryCreate(p);
            using var kill = token.Register(() => { try { p.Kill(true); } catch (Exception) { } });
            var err = p.StandardError.ReadToEndAsync();
            var text = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync(CancellationToken.None);
            await err;
            token.ThrowIfCancellationRequested();
            return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
