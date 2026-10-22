using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LimeWin.Core.Office;

/// <summary>
/// One STA thread and the Office app it is connected to (spec §7), late bound through <c>dynamic</c>. Work items run one
/// at a time. The app is acquired on the first item. Exactly one process appearing across <c>CreateInstance</c> is ours;
/// none means we attached to someone else's instance (<c>attachedToUser</c>): we never hide, quit or kill it, and only
/// switch its alerts and macro security off for the duration of each item. An instance of ours that is found holding a
/// document we did not open is from then on treated the same way.
/// The thread waits for work with a CLR wait, which on an STA thread pumps COM messages (CoWaitForMultipleHandles);
/// "busy" rejections are retried by <see cref="OfficeMessageFilter"/>.
/// </summary>
sealed class OfficeWorker
{
    static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(10);
    internal const string TimeoutMessage = "Office 응답이 없어 중단했습니다.";
    // Office here is unactivated but automatable; if that ever stops, starting it is where it fails.
    const string UnavailableMessage = "Office를 사용할 수 없습니다. 정품 인증 상태를 확인하세요.";
    const string AmbiguousStartMessage = "Office를 시작하지 못했습니다. 잠시 후 다시 시도하세요.";

    readonly string _progId;
    readonly string _processName;
    readonly TimeSpan _idleDelay;
    readonly bool _isExcel;
    readonly bool _isPowerPoint;
    readonly BlockingCollection<Action<OfficeWorker>> _queue = new();
    readonly Thread _thread;
    readonly object _gate = new();
    Timer? _idleTimer;
    bool _closed; // shut down or abandoned: no more idle timers

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

    public OfficeWorker(string progId, string processName, TimeSpan idleDelay)
    {
        _progId = progId;
        _processName = processName;
        _idleDelay = idleDelay;
        _isExcel = progId.StartsWith("Excel.", StringComparison.OrdinalIgnoreCase);
        _isPowerPoint = progId.StartsWith("PowerPoint.", StringComparison.OrdinalIgnoreCase);
        _thread = new Thread(Loop) { IsBackground = true, Name = $"LimeWin Office ({processName})" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    // Excel takes a bool, Word and PowerPoint their own enums (0 is not a PpAlertLevel).
    object AlertsOff => _isExcel ? false : _isPowerPoint ? PowerPointConst.AlertsNone : WordConst.AlertsNone;

    public OfficeProcess? LaunchedProcess { get { lock (_gate) return _launched; } }

    /// <summary>Queues an item; it receives the worker that runs it. Throws InvalidOperationException once closed.</summary>
    public void Add(Action<OfficeWorker> item) => _queue.Add(item);

    /// <summary>Called by an item before it starts its work, so a timeout can find it.</summary>
    public void MarkRunning(int itemId)
    {
        lock (_gate) _runningItem = itemId;
    }

    void Loop()
    {
        OfficeMessageFilter.Register(new OfficeMessageFilter(() => _retryLimitMs));
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable()) item(this);
        }
        finally { OfficeMessageFilter.Register(null); }
    }

    // ---- STA thread ----

    public T Execute<T>(Func<dynamic, T> work, TimeSpan timeout)
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
        var type = Type.GetTypeFromProgID(_progId) ?? throw new LimeException("Office를 찾을 수 없습니다.");

        var before = OfficeProcesses.Snapshot(_processName);
        lock (_gate) _acquiringSince = before;
        object? app;
        try { app = Activator.CreateInstance(type); }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException) { app = null; } // e.g. E_ACCESSDENIED, CO_E_SERVER_EXEC_FAILURE
        var added = OfficeProcesses.Added(before, OfficeProcesses.Snapshot(_processName));
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
            if (_isExcel)
            {
                // Started by automation, Excel is already hidden. Writing Visible (even false) makes an unactivated
                // Excel show its activation wizard on screen and answer busy from then on (measured), so only a
                // visible one is hidden.
                if ((bool)d.Visible) d.Visible = false;
                d.ScreenUpdating = false;
            }
            else if (!_isPowerPoint) // PowerPoint cannot be hidden (spec §7)
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
            if (_closed) return;
            _idleTimer ??= new Timer(_ => OnIdle());
            _idleTimer.Change(_idleDelay, Timeout.InfiniteTimeSpan);
        }
    }

    void OnIdle()
    {
        var armed = _armedGeneration;
        // The item may have been handed on to another worker by Abandon; it only ever releases this one.
        try { Add(w => { if (w == this && armed == _generation) Release(waitForQuit: false); }); }
        catch (InvalidOperationException) { } // queue completed by Shutdown or Abandon
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
    /// (it may have finished as the timeout fired, and Office may already be serving the next item).
    /// During acquisition the process that appeared since the snapshot is killed if it is exactly one.
    /// Returns false when the item is still running and nothing could be killed (the instance is someone else's, or the
    /// starting process is not identifiable): the thread then stays stuck for as long as Office does.
    /// </summary>
    public bool AbortRunning(int? itemId)
    {
        OfficeProcess? target = null;
        IReadOnlySet<OfficeProcess>? acquiringSince;
        lock (_gate)
        {
            if (itemId is { } id && _runningItem != id) return true;
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
            var added = OfficeProcesses.Added(acquiringSince, OfficeProcesses.Snapshot(_processName));
            if (added.Count == 1) target = added[0];
        }
        if (target is not { } t) return false;
        OfficeProcesses.Kill(t); // the stuck call then fails and the STA thread moves on
        return true;
    }

    /// <summary>Last item: release the app (quit it if ours and empty), then end the thread.</summary>
    public void Shutdown()
    {
        Close();
        _queue.Add(w => w.Release(waitForQuit: true));
        _queue.CompleteAdding();
    }

    /// <summary>
    /// Gives up on this worker while its thread is stuck: takes back the items still waiting (the caller hands them to a
    /// new worker) and lets the thread, should the stuck call ever return, release its app and end.
    /// </summary>
    public List<Action<OfficeWorker>> Abandon()
    {
        Close();
        var pending = new List<Action<OfficeWorker>>();
        while (_queue.TryTake(out var item)) pending.Add(item);
        _queue.Add(w => w.Release(waitForQuit: false));
        _queue.CompleteAdding();
        return pending;
    }

    void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _idleTimer?.Dispose();
        }
    }

    public bool Join(TimeSpan timeout) => _thread.Join(timeout);
}
