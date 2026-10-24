using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zest.App.Wheel;
using Zest.Core.Design;
using Microsoft.Win32;

namespace Zest.App.Tray;

/// <summary>
/// The tray glyph: the three wheels as one ring of three segments, lemon, tangerine and lime clockwise from 12 o'clock
/// (cut like the wheel's petals, with the same gutters), drawn with WPF geometry at each icon size so every size is
/// pixel-tuned rather than scaled. The fills are the same on both taskbars; on a light one each segment also gets a thin
/// edge in its ink, so the pale lemon keeps its shape against the near-white bar.
/// </summary>
public static class TrayIconArt
{
    public static readonly int[] Sizes = [16, 20, 24, 32];
    static readonly Citrus[] Segments = [ZestColors.Lemon, ZestColors.Tangerine, ZestColors.Lime];

    /// <summary>True when the taskbar uses the light theme (<c>SystemUsesLightTheme</c>; dark when unset, as on older Windows).</summary>
    public static bool LightTaskbar()
    {
        const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        return Registry.GetValue(key, "SystemUsesLightTheme", 0) is int light && light != 0;
    }

    /// <summary>The glyph at one pixel size.</summary>
    public static BitmapSource Render(int size, bool lightTaskbar)
    {
        var center = new Point(size / 2.0, size / 2.0);
        var outer = size / 2.0 - size / 32.0;   // 15.5 of 32: keeps the antialiased edge inside the square
        var band = size * 0.21;                 // ring thickness (3.4 px at 16, 6.7 at 32)
        var inner = outer - band;
        var gutter = Math.Max(1.0, size * 0.07);
        var outerCorner = size * 0.06;
        var innerCorner = size * 0.03;
        var edge = size <= 20 ? 0.75 : 1.0;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (var i = 0; i < Segments.Length; i++)
            {
                var citrus = Segments[i];
                var fill = Frozen(new SolidColorBrush(WheelStyle.ToColor(citrus.Fill)));
                var petal = PetalPath.Petal(center, i, Segments.Length, inner, outer, gutter, outerCorner, innerCorner);
                if (!lightTaskbar)
                {
                    dc.DrawGeometry(fill, null, petal);
                    continue;
                }
                // The edge sits inside the outline (the fill drawn over the stroke's outer half), so the size stays the same.
                var ink = Frozen(new SolidColorBrush(WheelStyle.ToColor(citrus.InkLight)));
                dc.DrawGeometry(ink, null, petal);
                dc.DrawGeometry(fill, null, Inset(petal, edge));
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary><paramref name="g"/> shrunk by <paramref name="by"/> on every side.</summary>
    static Geometry Inset(Geometry g, double by)
    {
        var outline = g.GetWidenedPathGeometry(new Pen(Brushes.Black, 2 * by));
        var inset = new CombinedGeometry(GeometryCombineMode.Exclude, g, outline);
        inset.Freeze();
        return inset;
    }

    static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>A multi-size .ico (PNG entries, 16/20/24/32 px).</summary>
    public static byte[] Ico(bool lightTaskbar)
    {
        var images = Sizes.Select(s => Png(Render(s, lightTaskbar))).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0); // reserved
        w.Write((short)1); // icon
        w.Write((short)images.Count);
        var offset = 6 + 16 * images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            w.Write((byte)Sizes[i]);
            w.Write((byte)Sizes[i]);
            w.Write((byte)0);  // palette
            w.Write((byte)0);  // reserved
            w.Write((short)1); // planes
            w.Write((short)32); // bits per pixel
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var png in images) w.Write(png);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>The tray icon at the notification area's current size (small-icon metric at the system DPI).</summary>
    public static System.Drawing.Icon Icon(bool lightTaskbar)
    {
        var side = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
        using var stream = new MemoryStream(Ico(lightTaskbar));
        return new System.Drawing.Icon(stream, side, side);
    }

    public static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    const int SM_CXSMICON = 49;
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
}
