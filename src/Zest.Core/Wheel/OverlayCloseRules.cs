namespace Zest.Core.Wheel;

public enum OverlayAction
{
    /// <summary>Leave the overlay up.</summary>
    Keep,
    /// <summary>The button is up after a FileDrop: stay up for OLE's Drop, which usually arrives after the hook's button-up.</summary>
    AwaitDrop,
    Close,
}

/// <summary>
/// When the drop overlay closes (docs/spike-notes.md). Pure: the caller feeds events and a monotonic clock and acts on the result.
/// - Button up without a FileDrop DragEnter, a DragEnter without FileDrop, a Drop, or Esc: close now.
/// - Button up after a FileDrop: await the Drop, but close <see cref="DropTimeout"/> after the first release regardless.
/// - The safety tick (button state polled every 100 ms) follows the same rule; repeats never restart the fallback.
/// </summary>
public sealed class OverlayCloseRules
{
    public static readonly TimeSpan DropTimeout = TimeSpan.FromMilliseconds(500);

    bool _filesReceived;
    TimeSpan? _awaitSince;

    public bool AwaitingDrop => _awaitSince.HasValue;

    /// <summary>Call when the overlay is shown for a new drag.</summary>
    public void Reset()
    {
        _filesReceived = false;
        _awaitSince = null;
    }

    public OverlayAction FileDragEntered()
    {
        _filesReceived = true;
        return AwaitingDrop ? OverlayAction.AwaitDrop : OverlayAction.Keep;
    }

    /// <summary>Not a file drag: get out of the way so OLE re-targets the window underneath.</summary>
    public OverlayAction NonFileDragEntered() => OverlayAction.Close;

    public OverlayAction ButtonUp(TimeSpan now)
    {
        if (!_filesReceived) return OverlayAction.Close;
        _awaitSince ??= now;
        return now - _awaitSince.Value >= DropTimeout ? OverlayAction.Close : OverlayAction.AwaitDrop;
    }

    public OverlayAction SafetyTick(bool buttonDown, TimeSpan now)
    {
        if (AwaitingDrop || !buttonDown) return ButtonUp(now);
        return OverlayAction.Keep;
    }

    public OverlayAction Dropped() => OverlayAction.Close;

    public OverlayAction Escape() => OverlayAction.Close;
}
