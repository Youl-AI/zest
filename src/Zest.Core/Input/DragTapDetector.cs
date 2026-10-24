using Zest.Core.Catalog;

namespace Zest.Core.Input;

[Flags]
public enum Modifiers { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

public readonly record struct InputSample(bool LeftButton, int X, int Y, Modifiers Mods);

/// <summary>Detects "tap a modifier key while dragging" from polled input samples.
/// Fires only on the sample where a modifier bit becomes newly set while dragging and the resulting set is exactly one key;
/// releasing a key, or keys held since before the drag, never fires.</summary>
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

        var newlyPressed = (s.Mods & ~_prevMods) != Modifiers.None;
        _prevMods = s.Mods;

        return Dragging && newlyPressed ? ModeFor(s.Mods) : null;
    }

    public static WheelMode? ModeFor(Modifiers m) => m switch
    {
        Modifiers.Shift => WheelMode.Convert,
        Modifiers.Alt => WheelMode.Tools,
        Modifiers.Ctrl => WheelMode.Actions,
        _ => null,
    };
}
