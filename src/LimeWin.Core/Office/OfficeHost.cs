using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LimeWin.Core.Office;

/// <summary>
/// Runs Office automation work items one at a time on a dedicated STA thread (spec §7), late bound through <c>dynamic</c>.
/// The app is acquired on the first work item. Exactly one process appearing across <c>CreateInstance</c> is ours; none
/// means we attached to someone else's instance (<c>attachedToUser</c>): we never hide, quit or kill it, and only switch
/// its alerts and macro security off for the duration of each work item. An instance of ours that is found holding a
/// document we did not open is from then on treated the same way.
/// The thread waits for work with a CLR wait, which on an STA thread pumps COM messages (CoWaitForMultipleHandles);
/// "busy" rejections are retried by <see cref="OfficeMessageFilter"/>.
/// </summary>
public sealed class OfficeHost(string progId, string processName) : IDisposable
{
    static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(10);
    static readonly TimeSpan DisposeJoin = TimeSpan.FromSeconds(20);
    const string TimeoutMessage = "Office 응답이 없어 중단했습니다.";
    // Office here is unactivated but automatable; if that ever stops, starting it is where it fails.
    const string UnavailableMessage = "Office를 사용할 수 없습니다. 정품 인증 상태를 확인하세요.";
    const string AmbiguousStartMessage = "Office를 시작하지 못했습니다. 잠시 후 다시 시도하세요.";

    readonly bool _isExcel = progId.StartsWith("Excel.", StringComparison.OrdinalIgnoreCase);
    readonly bool _isPowerPoint = progId.StartsWith("PowerPoint.", StringComparison.OrdinalIgnoreCase);
    readonly BlockingCollection<Action> _queue = new();
    object AlertsOff => _isExcel ? (object)false : WordConst.AlertsNone; // Excel takes a bool, Word/PowerPoint an enum
    readonly object _gate = new();
    Thread? _thread;
    Timer? _idleTimer;
    bool _disposed;
    int _nextItemId;

    // Guarded by _gate (read from any thread, written on the STA thread).
    OfficeProcess? _launched;
    bool _attachedToUser;
    bool _killed;
    IReadOnlySet<OfficeProcess>? _acquiringSince; // process snapshot taken before a CreateInstance still in progress
    int _runningItem; // id of the work item executing now, 0 when none

    // STA thread only.
    object? _app;
    KillOnExitJob? _job; // our launched process dies with us, even if we are killed
    int _generation;
    int _retryLimitMs = int.MaxValue;
    volatile int _armedGeneration;

    /// <summary>How long the app may sit unused before it is released (quit if ours and empty).</summary>
    internal TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The Office process this host started and is still connected to, if any.</summary>
    public OfficeProcess? LaunchedProcess { get { lock (_gate) return _launched; } }

    /// <summary>
    /// Runs <paramref name="work"/> on the STA thread with the app object. <paramref name="timeout"/> counts from the moment
    /// the item starts (not while it is queued). On timeout or cancellation while running, the Office process we started is
    /// killed (an attached instance is left alone) and <see cref="LimeException"/> / <see cref="OperationCanceledException"/>
    /// is thrown. The work must release the COM objects it obtains.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<dynamic, T> work, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const int Queued = 0, Running = 1, Abandoned = 2;
        var state = Queued;
        var id = Interlocked.Increment(ref _nextItemId);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A result that arrives after we gave up is never awaited; observe it so it is not reported as unobserved.
        _ = done.Task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        Enqueue(() =>
        {
            if (Interlocked.CompareExchange(ref state, Running, Queued) != Queued) { done.TrySetCanceled(); return; }
            lock (_gate) _runningItem = id; // before 'started', so a timeout always finds it
            started.TrySetResult();
            try { done.TrySetResult(Execute(work, timeout)); }
            catch (Exception e) { done.TrySetException(e); }
        });

        try { await started.Task.WaitAsync(ct); }
        catch (OperationCanceledException) when (Interlocked.CompareExchange(ref state, Abandoned, Queued) == Queued)
        {
            throw; // never started
        }
        catch (OperationCanceledException) { /* started meanwhile: handled as a running cancel below */ }

        using (var stopLimit = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            await Task.WhenAny(done.Task, Task.Delay(timeout, stopLimit.Token));
            stopLimit.Cancel(); // do not leave the timer running after a normal finish
        }
        if (done.Task.IsCompleted) return await done.Task;

        AbortRunning(id);
        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        throw new LimeException(TimeoutMessage);
    }

    void Enqueue(Action item)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is null)
            {
                _thread = new Thread(Loop) { IsBackground = true, Name = $"LimeWin Office ({processName})" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
            _queue.Add(item);
        }
    }

    void Loop()
    {
        OfficeMessageFilter.Register(new OfficeMessageFilter(() => _retryLimitMs));
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable()) item();
        }
        finally { OfficeMessageFilter.Register(null); }
    }

    // ---- STA thread ----

    T Execute<T>(Func<dynamic, T> work, TimeSpan timeout)
    {
        _generation++;
        _retryLimitMs = (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds);
        try
        {
            dynamic app = EnsureApp();
            bool attached;
            lock (_gate) attached = _attachedToUser;
            if (!attached) return work(app);

            // Someone else's instance: switch prompts and macros off only while we work, then put them back.
            object alerts = app.DisplayAlerts;
            object security = app.AutomationSecurity;
            app.DisplayAlerts = AlertsOff;
            app.AutomationSecurity = MsoConst.AutomationSecurityForceDisable;
            try { return work(app); }
            finally
            {
                try { app.DisplayAlerts = alerts; app.AutomationSecurity = security; }
                catch (COMException) { }
            }
        }
        finally
        {
            bool killed;
            lock (_gate)
            {
                _runningItem = 0;
                killed = _killed;
            }
            if (killed) Forget();
            else
            {
                StopOwningIfShared();
                ArmIdle();
            }
        }
    }

    object EnsureApp()
    {
        if (_app is not null) return _app;
        var type = Type.GetTypeFromProgID(progId) ?? throw new LimeException("Office를 찾을 수 없습니다.");

        var before = OfficeProcesses.Snapshot(processName);
        lock (_gate) _acquiringSince = before;
        object? app;
        try { app = Activator.CreateInstance(type); }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException) { app = null; } // e.g. E_ACCESSDENIED, CO_E_SERVER_EXEC_FAILURE
        var added = OfficeProcesses.Added(before, OfficeProcesses.Snapshot(processName));
        bool aborted;
        lock (_gate)
        {
            _acquiringSince = null;
            aborted = _killed; // the caller timed out or cancelled while CreateInstance ran
        }

        if (app is null || aborted)
        {
            if (app is not null) Marshal.FinalReleaseComObject(app);
            if (added.Count == 1) OfficeProcesses.Kill(added[0]); // started for us but never usable: do not orphan it
            throw new LimeException(app is null ? UnavailableMessage : TimeoutMessage);
        }
        if (added.Count > 1)
        {
            // One of them is probably ours, but which is unknowable: kill nothing, keep nothing.
            Marshal.FinalReleaseComObject(app);
            throw new LimeException(AmbiguousStartMessage);
        }

        var launched = added.Count == 1 ? added[0] : (OfficeProcess?)null;
        lock (_gate)
        {
            _launched = launched;
            _attachedToUser = launched is null;
        }
        _app = app;
        if (launched is null) return app;
        _job = KillOnExitJob.TryCreate(launched.Value);

        try
        {
            dynamic d = app;
            d.DisplayAlerts = AlertsOff;
            d.AutomationSecurity = MsoConst.AutomationSecurityForceDisable;
            if (!_isPowerPoint) // PowerPoint cannot be hidden (spec §7)
            {
                d.Visible = false;
                d.ScreenUpdating = false;
            }
        }
        catch (COMException)
        {
            OfficeProcesses.Kill(launched.Value);
            Forget();
            throw new LimeException(UnavailableMessage);
        }
        return app;
    }

    /// <summary>
    /// Our work items close what they open, so a document still open after one belongs to someone else (a file the user
    /// double-clicked that joined our instance). From then on the instance is not ours to quit or kill.
    /// </summary>
    void StopOwningIfShared()
    {
        if (_app is null) return;
        lock (_gate)
        {
            if (_attachedToUser || _launched is null) return;
        }
        int count;
        try { count = OpenDocumentCount(_app); }
        catch (COMException) { return; }
        if (count == 0) return;
        lock (_gate) _attachedToUser = true;
        _job?.Release();
        _job?.Dispose();
        _job = null;
    }

    void ArmIdle()
    {
        if (_app is null) return;
        _armedGeneration = _generation;
        lock (_gate)
        {
            if (_disposed) return;
            _idleTimer ??= new Timer(_ => OnIdle());
            _idleTimer.Change(IdleDelay, Timeout.InfiniteTimeSpan);
        }
    }

    void OnIdle()
    {
        var armed = _armedGeneration;
        try { Enqueue(() => { if (armed == _generation) Release(waitForQuit: false); }); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { } // queue completed by Dispose
    }

    /// <summary>
    /// Lets go of the app. Quits it only when we started it, still own it and it has no open document; otherwise just
    /// disconnects, so a document someone opened in the meantime is never closed.
    /// </summary>
    void Release(bool waitForQuit)
    {
        if (_app is null) return;
        dynamic app = _app;
        OfficeProcess? launched;
        bool attached;
        lock (_gate)
        {
            launched = _launched;
            attached = _attachedToUser;
        }
        var quit = false;
        if (launched is not null && !attached)
        {
            try
            {
                quit = OpenDocumentCount(app) == 0;
                if (quit)
                {
                    if (_isExcel || _isPowerPoint) app.Quit();
                    else app.Quit(SaveChanges: WordConst.DoNotSaveChanges);
                }
            }
            catch (COMException) { /* document state unknown: leave it running, without kill-on-close */ }
        }
        var job = _job;
        _job = null;
        if (!quit) job?.Release(); // left running on purpose: it must outlive us
        Forget();
        if (quit && launched is { } p)
        {
            if (waitForQuit) EnsureExited(p, job);
            else _ = Task.Run(() => EnsureExited(p, job));
        }
        else job?.Dispose();
    }

    static void EnsureExited(OfficeProcess p, KillOnExitJob? job)
    {
        if (!OfficeProcesses.WaitForExit(p, QuitWait)) OfficeProcesses.Kill(p);
        job?.Dispose();
    }

    int OpenDocumentCount(dynamic app)
    {
        dynamic collection = _isExcel ? app.Workbooks : _isPowerPoint ? app.Presentations : app.Documents;
        try { return (int)collection.Count; }
        finally { Marshal.FinalReleaseComObject(collection); }
    }

    /// <summary>Drops our reference to the app (no Quit) and resets ownership.</summary>
    void Forget()
    {
        if (_app is not null)
        {
            try { Marshal.FinalReleaseComObject(_app); }
            catch (Exception e) when (e is ArgumentException or InvalidComObjectException) { }
            _app = null;
        }
        _job?.Dispose(); // only still set when the process was killed or failed to start
        _job = null;
        lock (_gate)
        {
            _launched = null;
            _attachedToUser = false;
            _killed = false;
        }
        // Collect any runtime callable wrappers a work item left behind so the server sees no outstanding references.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    // ---- any thread ----

    /// <summary>
    /// Kills our Office so a stuck call returns. With <paramref name="itemId"/>, only while that item is still running
    /// (it may have finished as the timeout fired, and Word may already be serving the next item).
    /// During acquisition the process that appeared since the snapshot is killed if it is exactly one.
    /// </summary>
    void AbortRunning(int? itemId)
    {
        OfficeProcess? target = null;
        IReadOnlySet<OfficeProcess>? acquiringSince;
        lock (_gate)
        {
            if (itemId is { } id && _runningItem != id) return;
            acquiringSince = _acquiringSince;
            if (acquiringSince is not null) _killed = true;
            else if (!_attachedToUser && _launched is { } p)
            {
                target = p;
                _killed = true;
            }
        }
        if (acquiringSince is not null)
        {
            var added = OfficeProcesses.Added(acquiringSince, OfficeProcesses.Snapshot(processName));
            if (added.Count == 1) target = added[0];
        }
        if (target is { } t) OfficeProcesses.Kill(t); // the stuck call then fails and the STA thread moves on
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _idleTimer?.Dispose();
            thread = _thread;
            if (thread is not null)
            {
                _queue.Add(() => Release(waitForQuit: true));
                _queue.CompleteAdding();
            }
        }
        if (thread is null) return;
        if (!thread.Join(DisposeJoin))
        {
            AbortRunning(null); // the STA thread is stuck inside a call into our own Office
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
