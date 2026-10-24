using Zest.Core;
using Zest.Core.Jobs;
using Zest.Core.Output;

namespace Zest.App.Jobs;

/// <summary>A card's detail line for the files a job works on.</summary>
public static class JobDetail
{
    /// <summary>"사진.jpg", or "사진.jpg 외 2개" (<paramref name="counter"/> "장" for images where the copy counts sheets).</summary>
    public static string Of(IReadOnlyList<string> files, string counter = "개") =>
        files.Count == 1 ? OutputNaming.DisplayName(files[0]) : $"{OutputNaming.DisplayName(files[0])} 외 {files.Count - 1}{counter}";

    public static string Of(string file) => OutputNaming.DisplayName(file);
}

/// <summary>Runs operations in the background, each with its own progress card. Call on the UI thread.</summary>
public sealed class JobRunner(ProgressCardHost host)
{
    /// <summary>Running jobs (the work itself, which needs no UI thread to finish) and their cancel sources. UI thread only.</summary>
    readonly Dictionary<Task, CancellationTokenSource> _live = [];

    /// <param name="title">The task ("PNG 변환", "압축", "복사").</param>
    /// <param name="detail">What it works on ("사진.jpg", "사진.jpg 외 2개").</param>
    /// <param name="kind">The wheel it came from; the card takes that wheel's citrus.</param>
    public void Run(string title, string detail, JobKind kind, Func<OperationContext, Task<IReadOnlyList<string>>> work)
    {
        var cts = new CancellationTokenSource();
        var card = host.Add(title, detail, kind, cts.Cancel);
        // Progress<T> captures the UI SynchronizationContext here, so reports from worker threads land on the card safely.
        var ctx = new OperationContext(cts.Token, new Progress<double>(card.SetProgress), new Progress<bool>(card.SetWaiting));
        var job = Task.Run(() => work(ctx), cts.Token);
        _live[job] = cts;
        _ = ShowAsync(card, job, cts);
    }

    async Task ShowAsync(ProgressCard card, Task<IReadOnlyList<string>> job, CancellationTokenSource cts)
    {
        try
        {
            card.ShowDone(await job);
        }
        catch (OperationCanceledException)
        {
            card.ShowCancelled();
        }
        catch (ZestException e)
        {
            card.ShowFailed(e.Message);
        }
        catch (Exception e)
        {
            card.ShowFailed($"알 수 없는 오류: {e.Message}");
        }
        finally
        {
            _live.Remove(job);
            cts.Dispose();
        }
    }

    /// <summary>
    /// On app exit: cancels every running job and waits up to <paramref name="wait"/> for them to end, so their clean-up
    /// (deleting partial outputs, killing ffmpeg, closing our Office copies) runs before Office is released.
    /// </summary>
    public void CancelAll(TimeSpan wait)
    {
        if (_live.Count == 0) return;
        var jobs = _live.Keys.ToArray();
        foreach (var cts in _live.Values)
        {
            try { cts.Cancel(); }
            catch (AggregateException) { /* a cancel callback failed; the job still ends */ }
        }
        try { Task.WhenAll(jobs).Wait(wait); }
        catch (AggregateException) { /* failed or cancelled: either way it ended */ }
    }
}
