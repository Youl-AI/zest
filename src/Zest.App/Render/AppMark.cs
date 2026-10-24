using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Zest.App.Wheel;
using Zest.Core.Design;

namespace Zest.App.Render;

/// <summary>
/// Zest's app mark for the README headers, the social preview and icon-1024.png: a rounded tile (corners 22.5 % of the side)
/// holding the tray glyph's ring, three segments lemon, tangerine and lime clockwise from 12 o'clock, cut with
/// <see cref="PetalPath.Petal"/> like the wheel's petals, each lit from the top, inside a thin neutral rind. Light: a white
/// tile with a 1 px line and a soft shadow. Dark: an ink tile with a faint highlight along its top edge.
/// </summary>
public static class AppMark
{
    static readonly Citrus[] Segments = [ZestColors.Lemon, ZestColors.Tangerine, ZestColors.Lime];
    static readonly Color Ink = Color.FromRgb(0x1D, 0x1B, 0x16);

    /// <summary>The mark, <paramref name="side"/> DIP square; the light shadow draws outside it, so leave room around it.</summary>
    public static FrameworkElement Create(double side, bool dark, double shadowBlur = 0.16)
    {
        var corner = new CornerRadius(side * 0.225);
        var hairline = Math.Max(1, side / 140);
        var root = new Grid { Width = side, Height = side };

        // The shadow sits on its own layer so it does not blur the ring.
        var shadow = new Border
        {
            CornerRadius = corner,
            Background = new SolidColorBrush(dark ? Color.FromRgb(0x0C, 0x0B, 0x08) : Colors.White),
            Effect = new DropShadowEffect
            {
                Color = dark ? Colors.Black : Ink,
                Opacity = dark ? 0.45 : 0.10,
                BlurRadius = side * shadowBlur,
                ShadowDepth = side * 0.035,
                Direction = 270,
            },
        };
        root.Children.Add(shadow);

        var tile = new Border
        {
            CornerRadius = corner,
            BorderThickness = new Thickness(hairline),
            Background = dark
                ? new LinearGradientBrush(Color.FromRgb(0x26, 0x24, 0x1E), Color.FromRgb(0x1D, 0x1B, 0x16), 90)
                : new LinearGradientBrush(Colors.White, Color.FromRgb(0xFA, 0xFA, 0xF7), 90),
            // Dark: a highlight on the top edge fading down the sides. Light: the 1 px line of the cards.
            BorderBrush = dark
                ? new LinearGradientBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), 90)
                : new SolidColorBrush(Color.FromArgb(0x1A, 0x1D, 0x1B, 0x16)),
        };
        root.Children.Add(tile);
        root.Children.Add(Ring(side, dark));
        return root;
    }

    /// <summary>The three segments and the rind, on a canvas the size of the tile.</summary>
    static Canvas Ring(double side, bool dark)
    {
        var canvas = new Canvas { Width = side, Height = side };
        var center = new Point(side / 2, side / 2);
        var outer = side * 0.32;
        var inner = side * 0.175;
        var gap = side * 0.044;

        // The rind: a thin neutral circle just outside the segments.
        var rindRadius = outer + side * 0.042;
        canvas.Children.Add(new Ellipse
        {
            Width = 2 * rindRadius,
            Height = 2 * rindRadius,
            StrokeThickness = side * 0.011,
            Stroke = new SolidColorBrush(dark ? Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x1D, 0x1B, 0x16)),
            Margin = new Thickness(center.X - rindRadius, center.Y - rindRadius, 0, 0),
        });

        for (var i = 0; i < Segments.Length; i++)
        {
            var citrus = Segments[i];
            var fill = WheelStyle.ToColor(citrus.Fill);
            var petal = PetalPath.Petal(center, i, Segments.Length, inner, outer, gap, side * 0.03, side * 0.02);
            // One light from the top for the whole ring: each segment a little paler near the top of the tile.
            var brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, center.Y - outer),
                EndPoint = new Point(0, center.Y + outer),
                GradientStops = { new GradientStop(Mix(fill, Colors.White, 0.22), 0), new GradientStop(fill, 0.55), new GradientStop(Mix(fill, Ink, 0.06), 1) },
            };
            canvas.Children.Add(new Path
            {
                Data = petal,
                Fill = brush,
                // On white the pale lemon needs its ink edge to keep its shape, as on the light taskbar.
                Stroke = dark ? null : new SolidColorBrush(WithAlpha(WheelStyle.ToColor(citrus.InkLight), 0x30)),
                StrokeThickness = Math.Max(0.75, side * 0.005),
            });
        }
        return canvas;
    }

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
