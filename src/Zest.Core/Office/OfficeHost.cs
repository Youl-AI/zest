namespace Zest.Core.Office;

/// <summary>
/// Runs Office automation work items one at a time (spec §7) on an <see cref="OfficeWorker"/>: one STA thread connected
/// to one Office app. The worker and its thread are created on the first work item.
/// When a running item times out or is cancelled and the worker cannot end it by killing the process (the instance is
/// the user's, e.g. their PowerPoint, which is single-instance), its thread may stay stuck inside Office indefinitely.
/// The host then abandons that worker and moves the items still waiting, and every later one, to a fresh worker, so
/// one hung call cannot block all later jobs.
/// </summary>
public sealed class OfficeHost(string progId, string processName) : IDisposable
{
    static readonly TimeSpan DisposeJoin = TimeSpan.FromSeconds(20);

    readonly object _gate = new();
    readonly AttachedSettings _attachedSettings = new(); // shared by successive workers on the same instance
    OfficeWorker? _worker; // guarded by _gate
    bool _disposed;
    int _nextItemId;

    /// <summary>How long the app may sit unused before it is released (quit if ours and empty).</summary>
    internal TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The Office process the current worker started and is still connected to, if any.</summary>
    public OfficeProcess? LaunchedProcess
    {
        get
        {
            OfficeWorker? w;
            lock (_gate) w = _worker;
            return w?.LaunchedProcess;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the STA thread with the app object. <paramref name="timeout"/> counts from the moment
    /// the item starts (not while it is queued). On timeout or cancellation while running, the Office process we started is
    /// killed (an attached instance is left alone and its stuck thread abandoned) and <see cref="ZestException"/> /
    /// <see cref="OperationCanceledException"/> is thrown. The work must release the COM objects it obtains.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<dynamic, T> work, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const int Queued = 0, Running = 1, Abandoned = 2;
        var state = Queued;
        var id = Interlocked.Increment(ref _nextItemId);
        OfficeWorker? runner = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A result that arrives after we gave up is never awaited; observe it so it is not reported as unobserved.
        _ = done.Task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        Enqueue(w =>
        {
            if (Interlocked.CompareExchange(ref state, Running, Queued) != Queued) { done.TrySetCanceled(); return; }
            runner = w;
            w.MarkRunning(id); // before 'started', so a timeout always finds it
            started.TrySetResult();
            try { done.TrySetResult(w.Execute(work, timeout)); }
            catch (Exception e) { done.TrySetException(e); }
        });

        try { await started.Task.WaitAsync(ct); }
        catch (OperationCanceledException) when (Interlocked.CompareExchange(ref state, Abandoned, Queued) == Queued)
        {
            throw; // never started
        }
        catch (OperationCanceledException)
        {
            await started.Task; // started meanwhile: completes at once; handled as a running cancel below
        }

        using (var stopLimit = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            await Task.WhenAny(done.Task, Task.Delay(timeout, stopLimit.Token));
            stopLimit.Cancel(); // do not leave the timer running after a normal finish
        }
        if (done.Task.IsCompleted) return await done.Task;

        if (!runner!.AbortRunning(id)) Abandon(runner);
        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        throw new ZestException(OfficeWorker.TimeoutMessage);
    }

    void Enqueue(Action<OfficeWorker> item)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _worker ??= new OfficeWorker(progId, processName, IdleDelay, _attachedSettings);
            _worker.Add(item);
        }
    }

    /// <summary>Replaces a worker whose thread is stuck in a call we could not end; its waiting items move to the new one.</summary>
    void Abandon(OfficeWorker stuck)
    {
        lock (_gate)
        {
            if (_disposed || _worker != stuck) return;
            _worker = null;
            foreach (var item in stuck.Abandon())
            {
                _worker ??= new OfficeWorker(progId, processName, IdleDelay, _attachedSettings);
                _worker.Add(item);
            }
        }
    }

    public void Dispose()
    {
        OfficeWorker? worker;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            worker = _worker;
            worker?.Shutdown();
        }
        if (worker is null) return;
        // Stuck inside a call: killing our Office ends it; an instance that is not ours to kill is left to it.
        if (!worker.Join(DisposeJoin) && worker.AbortRunning(null)) worker.Join(TimeSpan.FromSeconds(5));
    }
}
