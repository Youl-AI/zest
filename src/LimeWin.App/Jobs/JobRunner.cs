using LimeWin.Core;
using LimeWin.Core.Jobs;

namespace LimeWin.App.Jobs;

/// <summary>Runs operations in the background, each with its own progress card. Call on the UI thread.</summary>
public sealed class JobRunner(ProgressCardHost host)
{
    /// <summary>Running jobs (the work itself, which needs no UI thread to finish) and their cancel sources. UI thread only.</summary>
    readonly Dictionary<Task, CancellationTokenSource> _live = [];

    public void Run(string title, Func<OperationContext, Task<IReadOnlyList<string>>> work)
    {
        var cts = new CancellationTokenSource();
        var card = host.Add(title, cts.Cancel);
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
        catch (LimeException e)
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
