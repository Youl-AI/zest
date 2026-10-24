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
    public void Ink_OnWash_PassesAA_BothThemes() // the wheel's hub chip: accent text on its tinted chip
    {
        foreach (var dark in new[] { false, true })
            foreach (var c in new[] { ZestColors.Lemon, ZestColors.Tangerine, ZestColors.Lime })
            {
                var ratio = Contrast.Ratio(c.Ink(dark), Over(c.Wash(dark), ZestColors.For(dark).Window));
                Assert.True(ratio >= 4.5, $"{c.Name} dark={dark}: {ratio:0.00}");
            }
    }

    [Fact]
    public void HintAndDanger_OnWindow_BothThemes()
    {
        foreach (var s in new[] { ZestColors.Light, ZestColors.Dark })
        {
            Assert.True(Contrast.Ratio(s.Text3, s.Window) >= 3.0);
            Assert.True(Contrast.Ratio(s.Danger, s.Window) >= 4.5);
        }
    }

    /// <summary><paramref name="fg"/> composited over the opaque <paramref name="bg"/>.</summary>
    static Argb Over(Argb fg, Argb bg)
    {
        var a = fg.A / 255.0;
        uint Mix(byte f, byte b) => (uint)Math.Round(f * a + b * (1 - a));
        return new Argb(0xFF000000u | Mix(fg.R, bg.R) << 16 | Mix(fg.G, bg.G) << 8 | Mix(fg.B, bg.B));
    }

    [Fact]
    public void ModeMapping()
    {
        Assert.Equal(ZestColors.Lemon, ZestColors.For(WheelMode.Convert));
        Assert.Equal(ZestColors.Tangerine, ZestColors.For(WheelMode.Tools));
        Assert.Equal(ZestColors.Lime, ZestColors.For(WheelMode.Actions));
    }

    [Fact]
    public void DangerWash_IsPinned()
    {
        Assert.Equal(Argb.Hex("#FCE6E2"), ZestColors.Light.DangerWash);
        Assert.Equal(Argb.Hex("#21FF7B6B"), ZestColors.Dark.DangerWash);
    }

    [Fact]
    public void Ratio_KnownPair()
    {
        Assert.Equal(21.0, Contrast.Ratio(Argb.Hex("#000000"), Argb.Hex("#FFFFFF")), 0.01);
    }
}
