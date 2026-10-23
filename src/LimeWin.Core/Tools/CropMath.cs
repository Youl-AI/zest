namespace LimeWin.Core.Tools;

/// <summary>What the pointer grabbed in the crop editor.</summary>
public enum CropHandle { Move, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>A numeric field of the crop editor.</summary>
public enum CropField { X, Y, Width, Height }

/// <summary>
/// Crop-selection rules in original image pixels (orientation applied): every result lies inside the image, is at least
/// 1 x 1, and keeps the aspect ratio (width / height) when one is given. Drags take the selection at drag start and the
/// total pointer delta, so rounding never accumulates.
/// </summary>
public static class CropMath
{
    public static PixelRect Full(int imageWidth, int imageHeight) => new(0, 0, imageWidth, imageHeight);

    /// <summary>Pulls any rect inside the image, at least 1 x 1.</summary>
    public static PixelRect Clamp(PixelRect r, int imageWidth, int imageHeight)
    {
        var w = Math.Clamp(r.Width, 1, imageWidth);
        var h = Math.Clamp(r.Height, 1, imageHeight);
        return new PixelRect(Math.Clamp(r.X, 0, imageWidth - w), Math.Clamp(r.Y, 0, imageHeight - h), w, h);
    }

    /// <summary>Moves the selection without resizing it; it stops at the image edges.</summary>
    public static PixelRect Move(PixelRect start, double dx, double dy, int imageWidth, int imageHeight) =>
        Clamp(start with { X = start.X + Round(dx), Y = start.Y + Round(dy) }, imageWidth, imageHeight);

    public static PixelRect Resize(PixelRect start, CropHandle handle, double dx, double dy, double? aspect, int imageWidth, int imageHeight)
    {
        if (handle == CropHandle.Move) return Move(start, dx, dy, imageWidth, imageHeight);
        start = Clamp(start, imageWidth, imageHeight);
        return aspect is { } a && a > 0
            ? ResizeLocked(start, handle, dx, dy, a, imageWidth, imageHeight)
            : ResizeFree(start, handle, dx, dy, imageWidth, imageHeight);
    }

    static PixelRect ResizeFree(PixelRect s, CropHandle handle, double dx, double dy, int iw, int ih)
    {
        int left = s.X, top = s.Y, right = s.X + s.Width, bottom = s.Y + s.Height;
        if (MovesLeft(handle)) left = Math.Clamp(left + Round(dx), 0, right - 1);
        if (MovesRight(handle)) right = Math.Clamp(right + Round(dx), left + 1, iw);
        if (MovesTop(handle)) top = Math.Clamp(top + Round(dy), 0, bottom - 1);
        if (MovesBottom(handle)) bottom = Math.Clamp(bottom + Round(dy), top + 1, ih);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    static PixelRect ResizeLocked(PixelRect s, CropHandle handle, double dx, double dy, double a, int iw, int ih)
    {
        double left = s.X, top = s.Y, right = s.X + s.Width, bottom = s.Y + s.Height;
        var minW = Math.Max(1, a);

        if (handle is CropHandle.Left or CropHandle.Right)
        {
            // Width follows the pointer; height follows the width around the selection's vertical centre.
            var w = handle == CropHandle.Right ? s.Width + dx : s.Width - dx;
            var room = handle == CropHandle.Right ? iw - left : right;
            w = Math.Clamp(w, minW, Math.Max(minW, Math.Min(room, ih * a)));
            var h = w / a;
            var x = handle == CropHandle.Right ? left : right - w;
            var y = Math.Clamp(s.Y + s.Height / 2.0 - h / 2, 0, ih - h);
            return Snap(x, y, w, a, iw, ih);
        }
        if (handle is CropHandle.Top or CropHandle.Bottom)
        {
            var h = handle == CropHandle.Bottom ? s.Height + dy : s.Height - dy;
            var room = handle == CropHandle.Bottom ? ih - top : bottom;
            var minH = Math.Max(1, 1 / a);
            h = Math.Clamp(h, minH, Math.Max(minH, Math.Min(room, iw / a)));
            var w = h * a;
            var y = handle == CropHandle.Bottom ? top : bottom - h;
            var x = Math.Clamp(s.X + s.Width / 2.0 - w / 2, 0, iw - w);
            return Snap(x, y, w, a, iw, ih);
        }

        // Corner: the opposite corner stays put; the larger of the two pointer moves decides the size.
        var growsRight = MovesRight(handle);
        var growsDown = MovesBottom(handle);
        var anchorX = growsRight ? left : right;
        var anchorY = growsDown ? top : bottom;
        var wantW = growsRight ? s.Width + dx : s.Width - dx;
        var wantH = growsDown ? s.Height + dy : s.Height - dy;
        var width = Math.Max(wantW, wantH * a);
        var roomW = growsRight ? iw - anchorX : anchorX;
        var roomH = growsDown ? ih - anchorY : anchorY;
        width = Math.Clamp(width, minW, Math.Max(minW, Math.Min(roomW, roomH * a)));
        var height = width / a;
        return Snap(growsRight ? anchorX : anchorX - width, growsDown ? anchorY : anchorY - height, width, a, iw, ih);
    }

    /// <summary>The largest rect of the aspect ratio inside the current selection, on the same centre. Null leaves the selection as is.</summary>
    public static PixelRect FitAspect(PixelRect current, double? aspect, int imageWidth, int imageHeight)
    {
        current = Clamp(current, imageWidth, imageHeight);
        if (aspect is not { } a || a <= 0) return current;
        double w = current.Width, h = w / a;
        if (h > current.Height)
        {
            h = current.Height;
            w = h * a;
        }
        var x = current.X + (current.Width - w) / 2;
        var y = current.Y + (current.Height - h) / 2;
        return Snap(x, y, w, a, imageWidth, imageHeight);
    }

    /// <summary>A typed value for one field; with an aspect ratio the other side follows, and both shrink if it would leave the image.</summary>
    public static PixelRect FromField(PixelRect current, CropField field, int value, double? aspect, int imageWidth, int imageHeight)
    {
        var r = Clamp(current, imageWidth, imageHeight);
        switch (field)
        {
            case CropField.X:
                return r with { X = Math.Clamp(value, 0, imageWidth - r.Width) };
            case CropField.Y:
                return r with { Y = Math.Clamp(value, 0, imageHeight - r.Height) };
        }

        var roomW = imageWidth - r.X;
        var roomH = imageHeight - r.Y;
        if (aspect is not { } a || a <= 0)
        {
            return field == CropField.Width
                ? r with { Width = Math.Clamp(value, 1, roomW) }
                : r with { Height = Math.Clamp(value, 1, roomH) };
        }

        double w = field == CropField.Width ? value : value * a;
        w = Math.Clamp(w, Math.Max(1, a), Math.Max(Math.Max(1, a), Math.Min(roomW, roomH * a)));
        return Snap(r.X, r.Y, w, a, imageWidth, imageHeight);
    }

    /// <summary>Rounds a ratio-locked rect to whole pixels (height from the rounded width) and keeps it inside the image.</summary>
    static PixelRect Snap(double x, double y, double w, double a, int iw, int ih)
    {
        var wi = Math.Clamp(Round(w), 1, iw);
        var hi = Math.Clamp(Round(wi / a), 1, ih);
        return Clamp(new PixelRect(Round(x), Round(y), wi, hi), iw, ih);
    }

    static int Round(double v) => (int)Math.Round(Math.Clamp(v, int.MinValue / 2.0, int.MaxValue / 2.0), MidpointRounding.AwayFromZero);

    static bool MovesLeft(CropHandle h) => h is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft;
    static bool MovesRight(CropHandle h) => h is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight;
    static bool MovesTop(CropHandle h) => h is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight;
    static bool MovesBottom(CropHandle h) => h is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight;
}
