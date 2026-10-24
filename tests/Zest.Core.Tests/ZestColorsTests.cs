using Zest.Core.Catalog;
using Zest.Core.Design;

namespace Zest.Core.Tests;

public class ZestColorsTests
{
    [Fact]
    public void Text_OnWindow_PassesAA_BothThemes()
    {
        foreach (var s in new[] { ZestColors.Light, ZestColors.Dark })
        {
            Assert.True(Contrast.Ratio(s.Text, s.Window) >= 4.5);
            Assert.True(Contrast.Ratio(s.Text2, s.Window) >= 4.5);
        }
    }

    [Fact]
    public void OnFill_OnEachCitrus_PassesAA()
    {
        foreach (var c in new[] { ZestColors.Lemon, ZestColors.Tangerine, ZestColors.Lime })
            Assert.True(Contrast.Ratio(c.OnFill, c.Fill) >= 4.5, c.Name);
    }

    [Fact]
    public void LightInk_OnWhite_PassesNonText()
    {
        var white = Argb.Hex("#FFFFFF");
        foreach (var c in new[] { ZestColors.Lemon, ZestColors.Tangerine, ZestColors.Lime })
            Assert.True(Contrast.Ratio(c.InkLight, white) >= 3.0, c.Name);
    }

    [Fact]
    public void ModeMapping()
    {
        Assert.Equal(ZestColors.Lemon, ZestColors.For(WheelMode.Convert));
        Assert.Equal(ZestColors.Tangerine, ZestColors.For(WheelMode.Tools));
        Assert.Equal(ZestColors.Lime, ZestColors.For(WheelMode.Actions));
    }

    [Fact]
    public void Ratio_KnownPair()
    {
        Assert.Equal(21.0, Contrast.Ratio(Argb.Hex("#000000"), Argb.Hex("#FFFFFF")), 0.01);
    }
}
