using System.Windows;
using System.Windows.Media;
using Zest.Core.Wheel;
using G = Zest.Core.Wheel.WheelGeometry;

namespace Zest.App.Wheel;

/// <summary>
/// Petal outlines matching <see cref="WheelGeometry.PetalAt"/>: ring sectors between Inner and Outer, separated by straight
/// gutters of width Gap, with every corner rounded by an exact fillet (outer 9, inner 5, as in the original).
/// </summary>
public static class PetalPath
{
    static readonly WheelGeometry Geometry = new();

    public static Point Center => new(G.CanvasSide / 2, G.CanvasSide / 2);

    /// <summary>Point at <paramref name="radius"/> along a bearing (radians, clockwise from 12 o'clock), in canvas units.</summary>
    public static Point At(double bearing, double radius) => At(Center, bearing, radius);

    public static double Bearing(int index, int count) => Geometry.BearingOf(index, count);

    /// <summary>Outward offset of the highlighted petal (none for a single ring petal).</summary>
    public static Vector LiftOf(int index, int count) =>
        count > 1 ? Unit(Bearing(index, count)) * WheelStyle.Lift : new Vector();

    /// <summary>The wheel's petal.</summary>
    public static Geometry Outline(int index, int count) =>
        Petal(Center, index, count, G.Inner, G.Outer, G.Gap, WheelStyle.OuterCorner, WheelStyle.InnerCorner);

    /// <summary>A rounded ring-sector petal around <paramref name="center"/>; also used for the tray icon's lime segments.</summary>
    public static Geometry Petal(Point center, int index, int count, double inner, double outer, double gap, double outerCorner, double innerCorner)
    {
        Geometry result;
        if (count == 1)
        {
            result = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(center, outer, outer),
                new EllipseGeometry(center, inner, inner));
            result.Freeze();
            return result;
        }

        var bearing = 2 * Math.PI * index / count;
        var half = Math.PI / count;
        var left = bearing - half;   // gutter on the counter-clockwise side
        var right = bearing + half;  // gutter on the clockwise side

        // Same clamping as the original: corners never eat more than 30 % of an edge.
        var outerSpan = (right - Math.Asin(gap / 2 / outer)) - (left + Math.Asin(gap / 2 / outer));
        var innerSpan = (right - Math.Asin(gap / 2 / inner)) - (left + Math.Asin(gap / 2 / inner));
        var oc = Math.Max(0, Math.Min(outerCorner, outerSpan * outer * 0.3));
        var ic = Math.Max(0, Math.Min(innerCorner, innerSpan * inner * 0.3));

        var lo = Fillet(left, +1, gap, outer - oc, oc, outer);
        var ro = Fillet(right, -1, gap, outer - oc, oc, outer);
        var li = Fillet(left, +1, gap, inner + ic, ic, inner);
        var ri = Fillet(right, -1, gap, inner + ic, ic, inner);

        Point P(Vector v) => center + v;
        var figure = new PathFigure { StartPoint = P(lo.OnSide), IsClosed = true, IsFilled = true };
        figure.Segments.Add(Arc(P(lo.OnCircle), oc, SweepDirection.Clockwise));
        figure.Segments.Add(Arc(P(ro.OnCircle), outer, SweepDirection.Clockwise));
        figure.Segments.Add(Arc(P(ro.OnSide), oc, SweepDirection.Clockwise));
        figure.Segments.Add(new LineSegment(P(ri.OnSide), true));
        figure.Segments.Add(Arc(P(ri.OnCircle), ic, SweepDirection.Clockwise));
        figure.Segments.Add(Arc(P(li.OnCircle), inner, SweepDirection.Counterclockwise));
        figure.Segments.Add(Arc(P(li.OnSide), ic, SweepDirection.Clockwise));
        result = new PathGeometry([figure]);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// Fillet of radius <paramref name="r"/> between a petal side (the line Gap/2 from the gutter at <paramref name="gutter"/>,
    /// on side <paramref name="side"/>) and a circle of radius <paramref name="circle"/>; its centre lies <paramref name="centerDistance"/>
    /// from the wheel centre (circle - r inside the outer edge, circle + r outside the inner edge). Returns both tangent points.
    /// </summary>
    static (Vector OnSide, Vector OnCircle) Fillet(double gutter, int side, double gap, double centerDistance, double r, double circle)
    {
        var u = Unit(gutter);
        var n = Normal(gutter) * side;
        var d = gap / 2 + r;
        var s = Math.Sqrt(centerDistance * centerDistance - d * d);
        var c = u * s + n * d;
        return (u * s + n * (gap / 2), c * (circle / centerDistance));
    }

    static ArcSegment Arc(Point to, double radius, SweepDirection sweep) =>
        new(to, new Size(radius, radius), 0, false, sweep, true);

    static Point At(Point center, double bearing, double radius) => center + Unit(bearing) * radius;

    /// <summary>Direction of a bearing on screen (y down).</summary>
    static Vector Unit(double bearing) => new(Math.Sin(bearing), -Math.Cos(bearing));

    /// <summary>Direction of increasing bearing (clockwise tangent).</summary>
    static Vector Normal(double bearing) => new(Math.Cos(bearing), Math.Sin(bearing));
}
