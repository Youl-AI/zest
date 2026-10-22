using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LimeWin.App.Wheel;
using Microsoft.Win32;

namespace LimeWin.App.Tray;

/// <summary>
/// The tray glyph: a monochrome lime slice (rind ring around six rounded segments, cut like the wheel's petals), drawn with
/// WPF geometry at each icon size so every size is pixel-tuned rather than scaled. Dark glyph for a light taskbar, white for a dark one.
/// </summary>
public static class TrayIconArt
{
    public static readonly int[] Sizes = [16, 20, 24, 32];
    const int Segments = 6;
    static readonly Color OnLight = Color.FromRgb(0x1F, 0x1F, 0x1F);
    static readonly Color OnDark = Colors.White;

    /// <summary>True when the taskbar uses the light theme (<c>SystemUsesLightTheme</c>; dark when unset, as on older Windows).</summary>
    public static bool LightTaskbar()
    {
        const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        return Registry.GetValue(key, "SystemUsesLightTheme", 0) is int light && light != 0;
    }

    /// <summary>The glyph at one pixel size.</summary>
    public static BitmapSource Render(int size, bool lightTaskbar)
    {
        var brush = new SolidColorBrush(lightTaskbar ? OnLight : OnDark);
        brush.Freeze();

        var center = new Point(size / 2.0, size / 2.0);
        var outer = size / 2.0 - size / 32.0;           // 15.5 of 32: keeps the antialiased edge inside the square
        var rind = Math.Max(1.25, size * 0.09);         // rind thickness
        var pith = Math.Max(1.0, size * 0.06);          // gap between rind and segments
        var segmentOuter = outer - rind - pith;
        var gutter = Math.Max(1.0, size * 0.07);
        // At 16 and 20 px rounded teardrops blur into dots: draw plain wedges that meet near the centre instead.
        var small = size <= 20;
        var segmentInner = small ? gutter * 1.2 : size * 0.08; // six gutters of width g close up at radius g
        var segmentOuterCorner = small ? size * 0.04 : size * 0.08;
        var segmentInnerCorner = small ? 0.01 : size * 0.02;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(center, outer, outer),
                new EllipseGeometry(center, outer - rind, outer - rind));
            dc.DrawGeometry(brush, null, ring);
            for (var i = 0; i < Segments; i++)
                dc.DrawGeometry(brush, null, PetalPath.Petal(center, i, Segments, segmentInner, segmentOuter, gutter, segmentOuterCorner, segmentInnerCorner));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
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
