using LimeWin.Core.Catalog;
using LimeWin.Core.Input;

namespace LimeWin.Core.Tests;

public class DragTapDetectorTests
{
    private static DragTapDetector Dragging()
    {
        var d = new DragTapDetector(4);
        d.Feed(new(true, 0, 0, Modifiers.None));
        d.Feed(new(true, 20, 0, Modifiers.None));
        return d;
    }

    [Fact] public void TapDuringDrag_FiresOnce() { var d = new DragTapDetector(4); d.Feed(new(true, 0, 0, Modifiers.None)); d.Feed(new(true, 10, 0, Modifiers.None)); Assert.Equal(WheelMode.Convert, d.Feed(new(true, 11, 0, Modifiers.Shift))); Assert.Null(d.Feed(new(true, 12, 0, Modifiers.Shift))); }
    [Fact] public void TapBeforeThreshold_Ignored() { var d = new DragTapDetector(4); d.Feed(new(true, 0, 0, Modifiers.None)); Assert.Null(d.Feed(new(true, 2, 0, Modifiers.Shift))); }
    [Fact] public void KeyHeldFromStart_DoesNotFire() { var d = new DragTapDetector(4); d.Feed(new(true, 0, 0, Modifiers.Ctrl)); Assert.Null(d.Feed(new(true, 20, 0, Modifiers.Ctrl))); }
    [Fact] public void ReleaseAndRetap_SwitchesWheel() { var d = Dragging(); d.Feed(new(true, 20, 0, Modifiers.Shift)); d.Feed(new(true, 20, 0, Modifiers.None)); Assert.Equal(WheelMode.Tools, d.Feed(new(true, 20, 0, Modifiers.Alt))); }
    [Fact] public void Combination_Ignored() { var d = Dragging(); Assert.Null(d.Feed(new(true, 20, 0, Modifiers.Shift | Modifiers.Ctrl))); }
    [Fact] public void NoButton_NeverFires() { var d = new DragTapDetector(4); Assert.Null(d.Feed(new(false, 50, 0, Modifiers.Shift))); }
    [Fact] public void ButtonUp_Resets() { var d = Dragging(); d.Feed(new(false, 20, 0, Modifiers.None)); Assert.False(d.Dragging); }

    [Fact] public void HeldShift_ThenCtrlPressedAndReleased_DoesNotFire()
    {
        var d = new DragTapDetector(4); d.Feed(new(true, 0, 0, Modifiers.Shift)); d.Feed(new(true, 20, 0, Modifiers.Shift));
        Assert.Null(d.Feed(new(true, 20, 0, Modifiers.Shift | Modifiers.Ctrl)));
        Assert.Null(d.Feed(new(true, 20, 0, Modifiers.Shift)));
    }
    [Fact] public void ComboThenReleaseOne_DoesNotFire()
    {
        var d = Dragging();
        Assert.Null(d.Feed(new(true, 20, 0, Modifiers.Shift | Modifiers.Ctrl)));
        Assert.Null(d.Feed(new(true, 20, 0, Modifiers.Ctrl)));
    }

    [Fact] public void VerticalMoveAlone_StartsDrag() { var d = new DragTapDetector(4); d.Feed(new(true, 0, 0, Modifiers.None)); d.Feed(new(true, 0, 5, Modifiers.None)); Assert.True(d.Dragging); }
    [Fact] public void MoveEqualToThreshold_DoesNotStartDrag() { var d = new DragTapDetector(4); d.Feed(new(true, 0, 0, Modifiers.None)); d.Feed(new(true, 4, 4, Modifiers.None)); Assert.False(d.Dragging); }

    [Theory]
    [InlineData(Modifiers.Shift, WheelMode.Convert)]
    [InlineData(Modifiers.Alt, WheelMode.Tools)]
    [InlineData(Modifiers.Ctrl, WheelMode.Actions)]
    public void ModeFor_Single(Modifiers m, WheelMode expected) => Assert.Equal(expected, DragTapDetector.ModeFor(m));

    [Theory]
    [InlineData(Modifiers.None)]
    [InlineData(Modifiers.Shift | Modifiers.Alt)]
    [InlineData(Modifiers.Shift | Modifiers.Alt | Modifiers.Ctrl)]
    public void ModeFor_NoneOrCombination_IsNull(Modifiers m) => Assert.Null(DragTapDetector.ModeFor(m));
}
