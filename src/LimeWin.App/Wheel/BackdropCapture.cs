using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LimeWin.App.Wheel;

/// <summary>
/// Copies the screen pixels behind the wheel so the view can blur them into frosted glass. Taken once, right before the
/// wheel window is shown. Plain <c>SRCCOPY</c> without <c>CAPTUREBLT</c>, so layered windows such as Explorer's drag
/// image are not baked into the glass.
/// </summary>
internal static class BackdropCapture
{
    const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, int rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    /// <summary>The screen inside a physical-pixel rectangle, or null if the capture failed (the view then uses an opaque disc).</summary>
    public static BitmapSource? Capture(Native.RECT area)
    {
        if (area.Width <= 0 || area.Height <= 0) return null;
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        var memory = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, area.Width, area.Height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) return null;
            var previous = SelectObject(memory, bitmap);
            var copied = BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, SRCCOPY);
            SelectObject(memory, previous);
            if (!copied) return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // A screen copy's alpha byte is undefined; force opaque pixels so the glass never turns see-through.
            var image = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            image.Freeze();
            return image;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
