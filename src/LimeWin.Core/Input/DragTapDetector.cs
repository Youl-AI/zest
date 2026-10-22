using LimeWin.Core.Catalog;

namespace LimeWin.Core.Input;

[Flags]
public enum Modifiers { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

public readonly record struct InputSample(bool LeftButton, int X, int Y, Modifiers Mods);

/// <summary>Detects "tap a modifier key while dragging" from polled input samples.</summary>
public sealed class DragTapDetector(int dragThresholdPx)
{
    private bool _hasStart;
    private int _startX, _startY;
    private Modifiers _prevMods;

    public bool Dragging { get; private set; }

    public WheelMode? Feed(InputSample s)
    {
        if (!s.LeftButton)
        {
            _hasStart = false;
            Dragging = false;
            _prevMods = Modifiers.None;
            return null;
        }

        if (!_hasStart)
        {
            _hasStart = true;
            _startX = s.X;
            _startY = s.Y;
            _prevMods = s.Mods;
            return null;
        }

        if (!Dragging && (Math.Abs(s.X - _startX) > dragThresholdPx || Math.Abs(s.Y - _startY) > dragThresholdPx))
            Dragging = true;

        var changed = s.Mods != _prevMods;
        _prevMods = s.Mods;

        if (Dragging && changed && s.Mods != Modifiers.None)
            return ModeFor(s.Mods);
        return null;
    }

    public static WheelMode? ModeFor(Modifiers m) => m switch
    {
        Modifiers.Shift => WheelMode.Convert,
        Modifiers.Alt => WheelMode.Tools,
        Modifiers.Ctrl => WheelMode.Actions,
        _ => null,
    };
}
