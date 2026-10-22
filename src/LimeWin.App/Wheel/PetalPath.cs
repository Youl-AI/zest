using System.Windows;
using System.Windows.Media;
using LimeWin.Core.Wheel;
using G = LimeWin.Core.Wheel.WheelGeometry;

namespace LimeWin.App.Wheel;

/// <summary>Petal outlines matching <see cref="WheelGeometry.PetalAt"/>: ring sectors between Inner and Outer, separated by straight gutters of width Gap.</summary>
public static class PetalPath
{
    static readonly WheelGeometry Geometry = new();

    public static Point Center => new(G.CanvasSide / 2, G.CanvasSide / 2);

    /// <summary>Point at <paramref name="radius"/> along a bearing (radians, clockwise from 12 o'clock), in canvas units.</summary>
    public static Point At(double bearing, double radius) =>
        new(Center.X + radius * Math.Sin(bearing), Center.Y - radius * Math.Cos(bearing));

    public static double Bearing(int index, int count) => Geometry.BearingOf(index, count);

    public static Geometry Outline(int index, int count)
    {
        if (count == 1)
        {
            return new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(Center, G.Outer, G.Outer),
                new EllipseGeometry(Center, G.Inner, G.Inner));
        }

        var bearing = Bearing(index, count);
        var half = Math.PI / count;
        // Each side edge lies on a line Gap/2 from the gutter's bisector, so its angular inset depends on the radius.
        var outerInset = Math.Asin(G.Gap / 2 / G.Outer);
        var innerInset = Math.Asin(G.Gap / 2 / G.Inner);

        var figure = new PathFigure { StartPoint = At(bearing - half + outerInset, G.Outer), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(At(bearing + half - outerInset, G.Outer), new Size(G.Outer, G.Outer), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(bearing + half - innerInset, G.Inner), true));
        figure.Segments.Add(new ArcSegment(At(bearing - half + innerInset, G.Inner), new Size(G.Inner, G.Inner), 0, false, SweepDirection.Counterclockwise, true));
        var path = new PathGeometry([figure]);
        path.Freeze();
        return path;
    }
}
