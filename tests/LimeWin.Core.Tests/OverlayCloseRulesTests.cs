using LimeWin.Core.Wheel;

namespace LimeWin.Core.Tests;

public class OverlayCloseRulesTests
{
    static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    static OverlayCloseRules WithFiles()
    {
        var rules = new OverlayCloseRules();
        Assert.Equal(OverlayAction.Keep, rules.FileDragEntered());
        return rules;
    }

    [Fact]
    public void ButtonUpBeforeAnyDragEnter_Closes() =>
        Assert.Equal(OverlayAction.Close, new OverlayCloseRules().ButtonUp(Ms(0)));

    [Fact]
    public void ButtonUpAfterFileDrop_AwaitsDrop()
    {
        var rules = WithFiles();
        Assert.Equal(OverlayAction.AwaitDrop, rules.ButtonUp(Ms(0)));
        Assert.True(rules.AwaitingDrop);
    }

    [Fact]
    public void DropWhileAwaiting_Closes()
    {
        var rules = WithFiles();
        rules.ButtonUp(Ms(0));
        Assert.Equal(OverlayAction.Close, rules.Dropped());
    }

    [Fact]
    public void AwaitWithoutDrop_ClosesAt500msFallback()
    {
        var rules = WithFiles();
        rules.ButtonUp(Ms(1000));
        Assert.Equal(OverlayAction.AwaitDrop, rules.SafetyTick(buttonDown: false, Ms(1499)));
        Assert.Equal(OverlayAction.Close, rules.SafetyTick(buttonDown: false, Ms(1500)));
    }

    [Fact]
    public void EscapeWhileAwaiting_Closes()
    {
        var rules = WithFiles();
        rules.ButtonUp(Ms(0));
        Assert.Equal(OverlayAction.Close, rules.Escape());
    }

    [Fact]
    public void RepeatedSafetyTicksAndButtonUps_DoNotRestartTheFallback()
    {
        var rules = WithFiles();
        Assert.Equal(OverlayAction.AwaitDrop, rules.SafetyTick(buttonDown: false, Ms(0))); // safety tick notices the release first
        for (var t = 100; t < 500; t += 100)
            Assert.Equal(OverlayAction.AwaitDrop, rules.SafetyTick(buttonDown: false, Ms(t)));
        Assert.Equal(OverlayAction.AwaitDrop, rules.ButtonUp(Ms(450))); // the hook's late button-up
        Assert.Equal(OverlayAction.Close, rules.SafetyTick(buttonDown: false, Ms(500)));
    }

    [Fact]
    public void FallbackStillAppliesIfTheButtonIsDownAgain()
    {
        var rules = WithFiles();
        rules.ButtonUp(Ms(0));
        Assert.Equal(OverlayAction.Close, rules.SafetyTick(buttonDown: true, Ms(500)));
    }

    [Fact]
    public void NonFileDragEnter_Closes() =>
        Assert.Equal(OverlayAction.Close, new OverlayCloseRules().NonFileDragEntered());

    [Fact]
    public void SafetyTick_ButtonHeld_Keeps()
    {
        Assert.Equal(OverlayAction.Keep, new OverlayCloseRules().SafetyTick(buttonDown: true, Ms(0)));
        Assert.Equal(OverlayAction.Keep, WithFiles().SafetyTick(buttonDown: true, Ms(0)));
    }

    [Fact]
    public void SafetyTick_ButtonReleasedWithoutFileDrop_Closes() =>
        Assert.Equal(OverlayAction.Close, new OverlayCloseRules().SafetyTick(buttonDown: false, Ms(0)));

    [Fact]
    public void Reset_ForgetsFilesAndAwait()
    {
        var rules = WithFiles();
        rules.ButtonUp(Ms(0));
        rules.Reset();
        Assert.False(rules.AwaitingDrop);
        Assert.Equal(OverlayAction.Close, rules.ButtonUp(Ms(10)));
    }
}
