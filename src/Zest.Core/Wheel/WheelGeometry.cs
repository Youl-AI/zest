namespace Zest.Core.Wheel;

/// <summary>Petal hit-testing for the radial wheel. Index 0 is at 12 o'clock, clockwise.</summary>
public sealed class WheelGeometry
{
    public const double CanvasSide = 320, Inner = 54, Outer = 128, Gap = 8;

    /// <summary>Centre bearing of a petal in radians, clockwise from 12 o'clock.</summary>
    public double BearingOf(int index, int count) => index * 2 * Math.PI / count;

    /// <summary>
    /// Petal under (dx, dy) relative to the wheel centre (y down), or null for the hub hole and outside the ring. The drawn
    /// gutters between petals are not dead space: each sector runs to the boundary, so a release in a gutter picks the
    /// nearest petal, and an exact boundary belongs to the next petal clockwise.
    /// </summary>
    public int? PetalAt(double dx, double dy, int count)
    {
        if (count <= 0) return null;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < Inner || r > Outer) return null;
        if (count == 1) return 0;

        var bearing = Math.Atan2(dx, -dy);
        if (bearing < 0) bearing += 2 * Math.PI;

        var sector = 2 * Math.PI / count;
        return (int)Math.Floor((bearing + sector / 2) / sector) % count;
    }
}
