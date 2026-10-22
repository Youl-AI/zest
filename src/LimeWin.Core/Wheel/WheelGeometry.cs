namespace LimeWin.Core.Wheel;

/// <summary>Petal hit-testing for the radial wheel. Index 0 is at 12 o'clock, clockwise.</summary>
public sealed class WheelGeometry
{
    public const double CanvasSide = 320, Inner = 54, Outer = 128, Gap = 5, Hole = 46;

    /// <summary>Centre bearing of a petal in radians, clockwise from 12 o'clock.</summary>
    public double BearingOf(int index, int count) => index * 2 * Math.PI / count;

    /// <summary>Petal under (dx, dy) relative to the wheel centre (y down), or null for hole, outside, or gutter.</summary>
    public int? PetalAt(double dx, double dy, int count)
    {
        if (count <= 0) return null;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < Inner || r > Outer) return null;
        if (count == 1) return 0;

        var bearing = Math.Atan2(dx, -dy);
        if (bearing < 0) bearing += 2 * Math.PI;

        var sector = 2 * Math.PI / count;
        var index = (int)Math.Round(bearing / sector) % count;
        var offset = Math.Abs(bearing - index * sector);
        if (index == 0 && bearing > Math.PI) offset = 2 * Math.PI - bearing;

        // Gutters are straight strips of width Gap, so the angular half-gutter shrinks with radius.
        var halfGutter = Math.Asin(Math.Min(1, Gap / 2 / r));
        return offset <= sector / 2 - halfGutter ? index : null;
    }
}
