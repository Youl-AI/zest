using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using LimeWin.Core.Input;

namespace LimeWin.App.Input;

/// <summary>
/// Global low-level mouse and keyboard hooks that only observe: every callback calls CallNextHookEx and nothing is swallowed.
/// Callbacks just update the button/modifier state and post an <see cref="InputSample"/> to the dispatcher.
/// Only Shift/Ctrl/Alt and Esc are looked at; no other key is read, stored or logged. Injected input is ignored.
/// </summary>
public sealed class LowLevelHooks : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
    const uint LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x01;
    const int VK_ESCAPE = 0x1B;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public int vkCode, scanCode; public uint flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public Native.POINT pt; public int mouseData; public uint flags, time; public IntPtr dwExtraInfo; }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);

    readonly Dispatcher _dispatcher;
    readonly HookProc _mouseProc, _keyboardProc; // held so the GC never collects the callbacks
    IntPtr _mouseHook, _keyboardHook;

    // Hook-side state. Both hooks run on the installing (UI) thread, so no locking is needed.
    bool _button;
    int _x, _y;
    bool _lShift, _rShift, _lCtrl, _rCtrl, _lAlt, _rAlt;
    bool _movePosted;

    /// <summary>Raised on the dispatcher whenever the button, the modifier set or (coalesced, button held) the cursor changes.</summary>
    public event Action<InputSample>? Sample;

    /// <summary>Raised on the dispatcher when Esc is pressed.</summary>
    public event Action? Escape;

    public LowLevelHooks(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    public bool Installed => _mouseHook != IntPtr.Zero;

    /// <summary>Installs both hooks. Must be called on the dispatcher thread (it pumps the hook messages).</summary>
    public void Install()
    {
        if (Installed) return;
        var module = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook == IntPtr.Zero) throw new Win32Exception();
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            var error = new Win32Exception();
            Uninstall();
            throw error;
        }
    }

    public void Uninstall()
    {
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = _keyboardHook = IntPtr.Zero;
        _button = _movePosted = false;
        _lShift = _rShift = _lCtrl = _rCtrl = _lAlt = _rAlt = false;
    }

    public void Dispose() => Uninstall();

    Modifiers Mods =>
        (_lShift || _rShift ? Modifiers.Shift : Modifiers.None)
        | (_lCtrl || _rCtrl ? Modifiers.Ctrl : Modifiers.None)
        | (_lAlt || _rAlt ? Modifiers.Alt : Modifiers.None);

    void Post()
    {
        var sample = new InputSample(_button, _x, _y, Mods);
        _dispatcher.BeginInvoke(() => Sample?.Invoke(sample));
    }

    IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
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
                        _button = true;
                        Post();
                        break;
                    case WM_LBUTTONUP:
                        _button = false;
                        Post();
                        break;
                    case WM_MOUSEMOVE when _button && !_movePosted:
                        // Latest-point coalescing: at most one move in flight; it reads the newest position when it runs.
                        _movePosted = true;
                        _dispatcher.BeginInvoke(() =>
                        {
                            _movePosted = false;
                            if (_button) Sample?.Invoke(new InputSample(true, _x, _y, Mods));
                        });
                        break;
                }
            }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
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
                        case 0xA0: _lShift = down; break;
                        case 0xA1: _rShift = down; break;
                        case 0xA2: _lCtrl = down; break;
                        case 0xA3: _rCtrl = down; break;
                        case 0xA4: _lAlt = down; break;
                        case 0xA5: _rAlt = down; break;
                    }
                    if (Mods != before) Post(); // auto-repeat and every non-modifier key post nothing
                }
            }
        }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }
}
