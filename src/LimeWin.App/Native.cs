using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LimeWin.App;

/// <summary>Win32 window placement in physical pixels (the process is PerMonitorV2-aware, so every coordinate here is physical).</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    const int SM_SWAPBUTTON = 23;
    const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    public const int SM_CXDRAG = 68;

    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    public static POINT CursorPos() => GetCursorPos(out var p) ? p : default;

    /// <summary>True while the primary (logical left) mouse button is physically held.</summary>
    public static bool PrimaryButtonDown()
    {
        // GetAsyncKeyState reads physical buttons, so honour swapped buttons.
        var vk = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public static RECT VirtualScreen()
    {
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        return new RECT { Left = x, Top = y, Right = x + GetSystemMetrics(SM_CXVIRTUALSCREEN), Bottom = y + GetSystemMetrics(SM_CYVIRTUALSCREEN) };
    }

    /// <summary>Bounds and work area of the monitor nearest to a physical point.</summary>
    public static (RECT Bounds, RECT Work) MonitorAt(int x, int y)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST), ref info);
        return (info.rcMonitor, info.rcWork);
    }

    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Physical pixels per DIP on the monitor nearest to a physical point (1.0 at 96 DPI).</summary>
    public static double ScaleAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
    }

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    public static RECT WindowRect(Window w) => GetWindowRect(Handle(w), out var r) ? r : default;

    /// <summary>Topmost, without activating, at a physical rect.</summary>
    public static void SetBounds(Window w, RECT r) =>
        SetWindowPos(Handle(w), HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE);

    /// <summary>Topmost, without activating, top-left corner at a physical point; size unchanged.</summary>
    public static void MoveTo(Window w, int x, int y) =>
        SetWindowPos(Handle(w), HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Tool window that never takes activation and lets the mouse and OLE drops fall through to the window below.</summary>
    public static void MakeClickThrough(Window w)
    {
        var hwnd = Handle(w);
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Tool window (no Alt+Tab entry) that never takes activation.</summary>
    public static void MakeNoActivate(Window w)
    {
        var hwnd = Handle(w);
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>
    /// Moves a SizeToContent window so its centre sits at a physical point, clamped inside that point's monitor.
    /// Moving first lets WPF rescale for the target monitor's DPI (PerMonitorV2); the second move uses the final size.
    /// </summary>
    public static RECT CenterOn(Window w, int x, int y)
    {
        var r = WindowRect(w);
        MoveTo(w, x - r.Width / 2, y - r.Height / 2);
        r = WindowRect(w);
        var mon = MonitorAt(x, y).Bounds;
        var left = Math.Clamp(x - r.Width / 2, mon.Left, Math.Max(mon.Left, mon.Right - r.Width));
        var top = Math.Clamp(y - r.Height / 2, mon.Top, Math.Max(mon.Top, mon.Bottom - r.Height));
        MoveTo(w, left, top);
        return WindowRect(w);
    }

    public const int WM_DPICHANGED = 0x02E0;
}
