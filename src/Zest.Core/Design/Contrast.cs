namespace Zest.Core.Design;

/// <summary>WCAG 2.x contrast ratio.</summary>
public static class Contrast
{
    /// <summary>Contrast of <paramref name="fg"/> over <paramref name="bg"/>, 1..21. A translucent foreground is composited over the background first; the background is taken as opaque.</summary>
    public static double Ratio(Argb fg, Argb bg)
    {
        var a = fg.A / 255.0;
        var r = fg.R * a + bg.R * (1 - a);
        var g = fg.G * a + bg.G * (1 - a);
        var b = fg.B * a + bg.B * (1 - a);
        var l1 = Luminance(r, g, b);
        var l2 = Luminance(bg.R, bg.G, bg.B);
        var (hi, lo) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (hi + 0.05) / (lo + 0.05);
    }

    static double Luminance(double r, double g, double b) =>
        0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);

    static double Linear(double channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
