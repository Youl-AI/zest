using System.Windows;
using LimeWin.Core.Catalog;
using LimeWin.Core.Input;

namespace LimeWin.App.Input;

/// <summary>Turns hook samples into drag gestures: a modifier tap while dragging, the button release, and Esc.</summary>
public sealed class DragSession
{
    readonly DragTapDetector _detector = new(Native.GetSystemMetrics(Native.SM_CXDRAG));
    bool _button;

    /// <summary>A modifier was tapped while dragging; the point is the cursor in physical screen pixels.</summary>
    public event Action<WheelMode, Point>? Tapped;

    public event Action? ButtonReleased;

    public event Action? Escape;

    public DragSession(LowLevelHooks hooks)
    {
        hooks.Sample += OnSample;
        hooks.Escape += () => Escape?.Invoke();
    }

    void OnSample(InputSample s)
    {
        var mode = _detector.Feed(s);
        if (mode is { } m) Tapped?.Invoke(m, new Point(s.X, s.Y));

        if (_button && !s.LeftButton) ButtonReleased?.Invoke();
        _button = s.LeftButton;
    }
}
