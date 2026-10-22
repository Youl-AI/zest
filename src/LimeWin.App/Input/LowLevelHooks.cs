using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using LimeWin.Core.Input;

namespace LimeWin.App.Input;

/// <summary>
/// Global low-level mouse and keyboard hooks that only observe: every callback calls CallNextHookEx and nothing is swallowed.
/// The hooks live on their own STA thread with a bare GetMessage pump, so a busy UI thread can never delay system input
/// or make Windows drop the hooks (LowLevelHooksTimeout). Callbacks only update button/modifier state, build an
/// <see cref="InputSample"/> and BeginInvoke it to the UI dispatcher.
/// Only Shift/Ctrl/Alt and Esc are looked at; no other key is read, stored or logged. Injected input is ignored.
/// </summary>
public sealed class LowLevelHooks : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
    const uint WM_QUIT = 0x12, PM_NOREMOVE = 0;
    const uint LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x01;
    const int VK_ESCAPE = 0x1B, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    /// <summary>System input this much newer than our last callback means the hooks are gone.</summary>
    const int DeadAfterMs = 2000;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public int vkCode, scanCode; public uint flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public Native.POINT pt; public int mouseData; public uint flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public Native.POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize, dwTime; }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);

    readonly Dispatcher _dispatcher;
    readonly HookProc _mouseProc, _keyboardProc; // held so the GC never collects the callbacks
    Thread? _thread;
    uint _threadId;

    // Hook-thread state: touched only on the hook thread.
    IntPtr _mouseHook, _keyboardHook;
    bool _button;
    int _x, _y;
    bool _lShift, _rShift, _lCtrl, _rCtrl, _lAlt, _rAlt;

    // Move coalescing, shared between the hook thread and the dispatcher.
    readonly Lock _moveLock = new();
    InputSample _pendingMove;
    bool _movePosted;

    int _lastCallbackTick; // Environment.TickCount of the latest callback (same clock as GetLastInputInfo)

    /// <summary>Raised on the dispatcher whenever the button, the modifier set or (coalesced, button held) the cursor changes.</summary>
    public event Action<InputSample>? Sample;

    /// <summary>Raised on the dispatcher when Esc is pressed.</summary>
    public event Action? Escape;

    /// <param name="dispatcher">The UI dispatcher that receives samples.</param>
    public LowLevelHooks(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    public bool Installed => _thread is not null;

    /// <summary>Starts the hook thread and installs both hooks; throws <see cref="Win32Exception"/> if Windows refuses.</summary>
    public void Install()
    {
        if (Installed) return;
        Exception? error = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() => HookThread(ready, e => error = e)) { IsBackground = true, Name = "LimeWin input hooks" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (error is not null)
        {
            thread.Join();
            throw error;
        }
        _thread = thread;
    }

    /// <summary>Unhooks and ends the hook thread.</summary>
    public void Uninstall()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    public void Dispose() => Uninstall();

    /// <summary>
    /// True when Windows recorded input more than 2 s after our hooks last ran, i.e. the hooks were most likely removed
    /// (e.g. LowLevelHooksTimeout). Input on the secure desktop can also trigger this; reinstalling then is harmless.
    /// </summary>
    public bool LooksDead()
    {
        if (!Installed) return false;
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return false;
        var gap = unchecked((int)(info.dwTime - (uint)Volatile.Read(ref _lastCallbackTick)));
        return gap > DeadAfterMs;
    }

    void HookThread(ManualResetEventSlim ready, Action<Exception> fail)
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE); // make sure the thread has a queue for WM_QUIT
        _button = false;
        _lShift = _rShift = _lCtrl = _rCtrl = _lAlt = _rAlt = false;
        Volatile.Write(ref _lastCallbackTick, Environment.TickCount);

        var module = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            fail(new Win32Exception());
            ready.Set();
            return;
        }
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            fail(new Win32Exception());
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            ready.Set();
            return;
        }
        ready.Set();

        // No windows live on this thread; the hook callbacks run from inside GetMessage until WM_QUIT.
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }

        UnhookWindowsHookEx(_mouseHook);
        UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = _keyboardHook = IntPtr.Zero;
    }

    Modifiers Mods =>
        (_lShift || _rShift ? Modifiers.Shift : Modifiers.None)
        | (_lCtrl || _rCtrl ? Modifiers.Ctrl : Modifiers.None)
        | (_lAlt || _rAlt ? Modifiers.Alt : Modifiers.None);

    void Post()
    {
        var sample = new InputSample(_button, _x, _y, Mods);
        _dispatcher.BeginInvoke(() => Sample?.Invoke(sample));
    }

    void PostMove()
    {
        bool post;
        lock (_moveLock)
        {
            _pendingMove = new InputSample(true, _x, _y, Mods);
            post = !_movePosted;
            _movePosted = true;
        }
        if (post) _dispatcher.BeginInvoke(DeliverMove);
    }

    // Latest-point coalescing: at most one move is queued; it delivers the newest position when it runs.
    void DeliverMove()
    {
        InputSample sample;
        lock (_moveLock)
        {
            sample = _pendingMove;
            _movePosted = false;
        }
        Sample?.Invoke(sample);
    }

    /// <summary>Key-up events can be missed (secure desktop, UIPI); a new press re-reads the real modifier state.</summary>
    void ResyncModifiers()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        _lShift = Down(VK_LSHIFT);
        _rShift = Down(VK_RSHIFT);
        _lCtrl = Down(VK_LCONTROL);
        _rCtrl = Down(VK_RCONTROL);
        _lAlt = Down(VK_LMENU);
        _rAlt = Down(VK_RMENU);
    }

    IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        Volatile.Write(ref _lastCallbackTick, Environment.TickCount);
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((data.flags & LLMHF_INJECTED) == 0)
            {
                _x = data.pt.X;
                _y = data.pt.Y;
                switch ((int)wParam)
                {
                    case WM_LBUTTONDOWN:
                        ResyncModifiers();
                        _button = true;
                        Post();
                        break;
                    case WM_LBUTTONUP:
                        _button = false;
                        Post();
                        break;
                    case WM_MOUSEMOVE when _button:
                        PostMove();
                        break;
                }
            }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        Volatile.Write(ref _lastCallbackTick, Environment.TickCount);
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            var up = msg is WM_KEYUP or WM_SYSKEYUP;
            if ((data.flags & LLKHF_INJECTED) == 0 && (down || up))
            {
                if (down && data.vkCode == VK_ESCAPE)
                {
                    _dispatcher.BeginInvoke(() => Escape?.Invoke());
                }
                else
                {
                    var before = Mods;
                    switch (data.vkCode)
                    {
                        case VK_LSHIFT: _lShift = down; break;
                        case VK_RSHIFT: _rShift = down; break;
                        case VK_LCONTROL: _lCtrl = down; break;
                        case VK_RCONTROL: _rCtrl = down; break;
                        case VK_LMENU: _lAlt = down; break;
                        case VK_RMENU: _rAlt = down; break;
                    }
                    if (Mods != before) Post(); // auto-repeat and every non-modifier key post nothing
                }
            }
        }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }
}
