using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Zest.Core.Jobs;

namespace Zest.Core.Convert;

/// <summary>Runs ffmpeg and ffprobe: tied to Zest's lifetime, killed on cancel, errors in Korean.</summary>
internal static class FfmpegProcess
{
    const int StderrLines = 20;

    /// <summary>
    /// Runs ffmpeg to completion. With <paramref name="duration"/> and <c>-progress pipe:1</c> in the arguments, reports progress to <paramref name="ctx"/>.
    /// A failure reads "{<paramref name="failure"/>}: {last stderr line}"; the caller picks the prefix (conversion or metadata).
    /// </summary>
    public static async Task RunAsync(string ffmpegPath, IReadOnlyList<string> args, double? duration, OperationContext ctx, string failure = "변환 실패")
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
        catch (System.ComponentModel.Win32Exception) { throw new ZestException($"{failure}: ffmpeg를 실행할 수 없습니다."); }
        using var tiedToUs = Office.KillOnExitJob.TryCreate(process); // if Zest dies, ffmpeg goes with it

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
            throw new ZestException($"{failure}: {tail.LastOrDefault() ?? $"종료 코드 {process.ExitCode}"}");
    }

    /// <summary>Runs ffprobe (or any tool) and returns its standard output; null when it cannot be started.</summary>
    public static async Task<string?> CaptureAsync(string exePath, IReadOnlyList<string> args, CancellationToken token)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
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
            return text;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
