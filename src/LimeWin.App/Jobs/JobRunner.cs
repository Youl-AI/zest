using LimeWin.Core;
using LimeWin.Core.Jobs;

namespace LimeWin.App.Jobs;

/// <summary>Runs one operation in the background with its own progress card. Call on the UI thread.</summary>
public sealed class JobRunner(ProgressCardHost host)
{
    public void Run(string title, Func<OperationContext, Task<IReadOnlyList<string>>> work)
    {
        var cts = new CancellationTokenSource();
        var card = host.Add(title, cts.Cancel);
        // Progress<T> captures the UI SynchronizationContext here, so reports from worker threads land on the card safely.
        var ctx = new OperationContext(cts.Token, new Progress<double>(card.SetProgress));
        _ = RunAsync(card, ctx, work);
    }

    static async Task RunAsync(ProgressCard card, OperationContext ctx, Func<OperationContext, Task<IReadOnlyList<string>>> work)
    {
        try
        {
            var results = await Task.Run(() => work(ctx), ctx.Token);
            card.ShowDone(results);
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
    }
}
