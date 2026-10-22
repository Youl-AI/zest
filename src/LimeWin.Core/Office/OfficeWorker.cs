using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LimeWin.Core.Office;

/// <summary>
/// One STA thread and the Office app it is connected to (spec §7), late bound through <c>dynamic</c>. Work items run one
/// at a time. The app is acquired on the first item. Exactly one process appearing across <c>CreateInstance</c> is ours;
/// none means we attached to someone else's instance (<c>attachedToUser</c>): we never hide, quit or kill it, and only
/// switch its alerts and macro security off while our items run (<see cref="AttachedSettings"/>). An instance of ours
/// that is found holding a document we did not open, or for PowerPoint (single-instance) showing any window, has the
/// user in it and is from then on treated the same way.
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
    readonly AttachedSettings _settings;
    readonly bool _isExcel;
    readonly bool _isPowerPoint;
    readonly BlockingCollection<Action<OfficeWorker>> _queue = new();
    readonly Thread _thread;
    readonly object _gate = new();
    Timer? _idleTimer;
    bool _closed; // shut down or abandoned: no more idle timers

    // Guarded by _gate (read from any thread, written on the STA thread unless noted).
    OfficeProcess? _launched;
    bool _attachedToUser; // also set by AbortRunning when the user turns out to be in our PowerPoint
    bool _killed;         // the running item was given up: what it holds or still acquires is let go, never kept
    IReadOnlySet<OfficeProcess>? _acquiringSince; // process snapshot taken before a CreateInstance still in progress
    int _runningItem; // id of the work item executing now, 0 when none
    KillOnExitJob? _job; // our launched process dies with us, even if we are killed; let go by AbortRunning too

    // STA thread only.
    object? _app;
    int _generation;
    int _retryLimitMs = int.MaxValue;
    volatile int _armedGeneration;

    public OfficeWorker(string progId, string processName, TimeSpan idleDelay, AttachedSettings settings)
    {
        _progId = progId;
        _processName = processName;
        _idleDelay = idleDelay;
        _settings = settings;
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

            // Someone else's instance: prompts and macros are off only while items of ours run on it.
            _settings.Enter(app);
            try
            {
                app.DisplayAlerts = AlertsOff;
                app.AutomationSecurity = MsoConst.AutomationSecurityForceDisable;
                return work(app);
            }
            finally { _settings.Exit(app); }
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
        lock (_gate)
        {
            if (_killed) throw new LimeException(TimeoutMessage); // given up before it started: start nothing
            _acquiringSince = before;
        }
        object? app;
        try { app = Activator.CreateInstance(type); }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException) { app = null; } // e.g. E_ACCESSDENIED, CO_E_SERVER_EXEC_FAILURE
        var added = OfficeProcesses.Added(before, OfficeProcesses.Snapshot(_processName));
        var launched = added.Count == 1 ? added[0] : (OfficeProcess?)null;
        bool aborted;
        lock (_gate)
        {
            _acquiringSince = null;
            aborted = _killed; // the caller timed out or cancelled while CreateInstance ran
            if (!aborted && app is not null && added.Count <= 1)
            {
                _launched = launched;
                _attachedToUser = launched is null;
            }
        }

        if (aborted)
        {
            // AbortRunning already killed the one process it could identify at that moment. A process that appeared
            // since may belong to the worker that replaced this one, so nothing more is killed here.
            if (app is not null) Marshal.FinalReleaseComObject(app);
            throw new LimeException(TimeoutMessage);
        }
        if (app is null)
        {
            if (launched is { } orphan) OfficeProcesses.Kill(orphan); // started for us but never usable: do not orphan it
            throw new LimeException(UnavailableMessage);
        }
        if (added.Count > 1)
        {
            // One of them is probably ours, but which is unknowable: kill nothing, keep nothing.
            Marshal.FinalReleaseComObject(app);
            throw new LimeException(AmbiguousStartMessage);
        }

        _app = app;
        if (launched is null) return app;
        var job = KillOnExitJob.TryCreate(launched.Value);
        lock (_gate) _job = job;

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
    /// double-clicked that joined our instance); in PowerPoint the user may also be in it with no presentation yet.
    /// From then on the instance is not ours to quit or kill, and it must outlive us.
    /// </summary>
    void StopOwningIfShared()
    {
        if (_app is null) return;
        lock (_gate)
        {
            if (_attachedToUser || _launched is null) return;
        }
        bool shared;
        try { shared = UserInside(_app) || OpenDocumentCount(_app) > 0; }
        catch (COMException) { return; }
        if (shared) MarkShared();
    }

    void MarkShared()
    {
        KillOnExitJob? job;
        lock (_gate)
        {
            _attachedToUser = true;
            job = _job;
            _job = null;
        }
        job?.Release();
        job?.Dispose();
    }

    /// <summary>
    /// PowerPoint only (single-instance: a user who starts PowerPoint joins our process): the user is in our instance
    /// when it shows any window, is visible, or has a document window, even with no presentation open (start screen,
    /// Backstage). Windows are counted first, without calling in, so a busy instance is recognized too.
    /// </summary>
    bool UserInside(dynamic app)
    {
        if (!_isPowerPoint) return false;
        OfficeProcess? launched;
        lock (_gate) launched = _launched;
        if (launched is { } p && OfficeProcesses.VisibleWindowCount(p) > 0) return true;
        if ((int)app.Visible == MsoConst.True) return true;
        dynamic windows = app.Windows;
        try { return (int)windows.Count > 0; }
        finally { Marshal.FinalReleaseComObject(windows); }
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
    /// Lets go of the app. Quits it only when we started it, still own it, nobody is in it and it has no open document;
    /// otherwise just disconnects, so nothing someone opened in the meantime is ever closed.
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
                quit = !UserInside(app) && OpenDocumentCount(app) == 0;
                if (quit)
                {
                    if (_isExcel || _isPowerPoint) app.Quit();
                    else app.Quit(SaveChanges: WordConst.DoNotSaveChanges);
                }
            }
            catch (COMException) { /* state unknown: leave it running, without kill-on-close */ }
        }
        KillOnExitJob? job;
        lock (_gate)
        {
            job = _job;
            _job = null;
        }
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
        KillOnExitJob? job;
        lock (_gate)
        {
            job = _job; // only still set when the process was killed or failed to start
            _job = null;
            _launched = null;
            _attachedToUser = false;
            _killed = false;
        }
        job?.Dispose();
        // Collect any runtime callable wrappers a work item left behind so the server sees no outstanding references.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    // ---- any thread ----

    /// <summary>
    /// Gives up the running item and kills our Office so a stuck call returns. With <paramref name="itemId"/>, only
    /// while that item is still running (it may have finished as the timeout fired, and Office may already be serving the
    /// next item). During acquisition the process that appeared since the snapshot is killed if it is exactly one.
    /// A PowerPoint of ours that shows any window has the user in it: it is not killed, and its kill-on-close is
    /// released. Returns false when the item is still running and nothing was killed (the instance is someone else's,
    /// or the starting process is not identifiable): the thread then stays stuck for as long as Office does.
    /// </summary>
    public bool AbortRunning(int? itemId)
    {
        OfficeProcess? target = null;
        IReadOnlySet<OfficeProcess>? acquiringSince;
        var userInside = false;
        lock (_gate)
        {
            if (itemId is { } id && _runningItem != id) return true;
            _killed = true;
            acquiringSince = _acquiringSince;
            if (acquiringSince is null && !_attachedToUser && _launched is { } p)
            {
                userInside = _isPowerPoint && OfficeProcesses.VisibleWindowCount(p) > 0;
                if (!userInside) target = p;
            }
        }
        if (userInside) MarkShared();
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
