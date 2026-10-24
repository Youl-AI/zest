using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zest.App.Wheel;
using Zest.Core.Tools;

namespace Zest.App.Tools;

/// <summary>
/// Draws the image preview with the crop selection on top and lets the pointer move and resize it.
/// The selection is kept in original pixels; the displayed scale is recomputed from the element's current size on every
/// render and pointer event (DIP per original pixel = shown image width / original width). WPF pointer positions and
/// layout share DIP units on every monitor, so the mapping holds at any DPI and after a resize or DPI change; the
/// downscaled preview bitmap only affects sharpness, never coordinates. The selection follows the pointer 1:1, unanimated.
/// </summary>
public sealed class CropCanvas : FrameworkElement
{
    /// <summary>Room around the image so handles on the image edge are fully visible and grabbable.</summary>
    const double Inset = 16;

    BitmapSource? _image;
    int _sourceW = 1, _sourceH = 1;
    PixelRect _selection;
    (CropHandle Handle, Point Start, PixelRect From)? _drag;
    static readonly Brush Shade = Frozen(new SolidColorBrush(WheelStyle.CropShade));
    static readonly Pen OutlineShadow = Frozen(new Pen(Frozen(new SolidColorBrush(WheelStyle.CropOutlineShadow)), 1));
    static readonly Pen HandleStroke = Frozen(new Pen(Brushes.White, 1));
    Brush _accent = Brushes.Transparent;
    Pen _outline = new(Brushes.Transparent, 0);

    /// <summary>The selection changed under the pointer.</summary>
    public event Action<PixelRect>? SelectionChanged;

    public CropCanvas()
    {
        Focusable = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        SetAccent(WheelStyle.CropAccent);
    }

    /// <summary>Width / height to keep while dragging, or null for free.</summary>
    public double? Aspect { get; set; }

    /// <summary>Outline and handle color (<see cref="WheelStyle.CropAccent"/> unless a caller overrides it).</summary>
    public void SetAccent(Color accent)
    {
        _accent = Frozen(new SolidColorBrush(accent));
        _outline = Frozen(new Pen(_accent, WheelStyle.CropOutline));
        InvalidateVisual();
    }

    public PixelRect Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            InvalidateVisual();
        }
    }

    /// <param name="preview">Downscaled, orientation-applied preview.</param>
    /// <param name="sourceWidth">Original width in pixels, the unit of <see cref="Selection"/>.</param>
    public void SetImage(BitmapSource preview, int sourceWidth, int sourceHeight)
    {
        _image = preview;
        _sourceW = Math.Max(1, sourceWidth);
        _sourceH = Math.Max(1, sourceHeight);
        _selection = CropMath.Full(_sourceW, _sourceH);
        InvalidateVisual();
    }

    /// <summary>Where the whole image is shown, in DIP: fitted inside the element minus the inset, centred.</summary>
    Rect ImageRect()
    {
        var w = Math.Max(1, ActualWidth - 2 * Inset);
        var h = Math.Max(1, ActualHeight - 2 * Inset);
        var scale = Math.Min(w / _sourceW, h / _sourceH);
        var iw = _sourceW * scale;
        var ih = _sourceH * scale;
        return new Rect((ActualWidth - iw) / 2, (ActualHeight - ih) / 2, iw, ih);
    }

    /// <summary>Original pixels to DIP.</summary>
    Rect ToDisplay(PixelRect r, Rect image)
    {
        var s = image.Width / _sourceW;
        return new Rect(image.X + r.X * s, image.Y + r.Y * s, r.Width * s, r.Height * s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // A transparent fill so the whole element takes the pointer, not only the painted parts.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_image is null) return;

        var image = ImageRect();
        dc.DrawImage(_image, image);

        var sel = ToDisplay(_selection, image);
        var shade = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(image), new RectangleGeometry(sel));
        dc.DrawGeometry(Shade, null, shade);

        var half = WheelStyle.CropOutline / 2;
        dc.DrawRectangle(null, OutlineShadow, Inflate(sel, half + 0.5));
        dc.DrawRectangle(null, _outline, sel);

        foreach (var (_, center) in Handles(sel))
        {
            var size = WheelStyle.CropHandleSize;
            var box = new Rect(center.X - size / 2, center.Y - size / 2, size, size);
            dc.DrawRoundedRectangle(_accent, HandleStroke, box, WheelStyle.CropHandleCorner, WheelStyle.CropHandleCorner);
        }
    }

    /// <summary>Corner handles always; side handles only when the side is long enough to tell them apart from the corners.</summary>
    static IEnumerable<(CropHandle Handle, Point Center)> Handles(Rect r)
    {
        double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        yield return (CropHandle.TopLeft, r.TopLeft);
        yield return (CropHandle.TopRight, r.TopRight);
        yield return (CropHandle.BottomLeft, r.BottomLeft);
        yield return (CropHandle.BottomRight, r.BottomRight);
        var room = WheelStyle.CropHandleHit * 2;
        if (r.Width >= room)
        {
            yield return (CropHandle.Top, new Point(cx, r.Top));
            yield return (CropHandle.Bottom, new Point(cx, r.Bottom));
        }
        if (r.Height >= room)
        {
            yield return (CropHandle.Left, new Point(r.Left, cy));
            yield return (CropHandle.Right, new Point(r.Right, cy));
        }
    }

    /// <summary>Handles first (20 DIP hit squares), then the selection's edges, then its inside (move).</summary>
    CropHandle? HitTest(Point p)
    {
        if (_image is null) return null;
        var sel = ToDisplay(_selection, ImageRect());
        var reach = WheelStyle.CropHandleHit / 2;
        // Bottom-right is tried first: on a tiny selection the handles overlap, and that one lets the user grow it again.
        foreach (var (handle, center) in Handles(sel).OrderBy(h => h.Handle == CropHandle.BottomRight ? 0 : 1))
            if (Math.Abs(p.X - center.X) <= reach && Math.Abs(p.Y - center.Y) <= reach) return handle;

        var edge = 4.0;
        var inX = p.X >= sel.Left - edge && p.X <= sel.Right + edge;
        var inY = p.Y >= sel.Top - edge && p.Y <= sel.Bottom + edge;
        if (inY && Math.Abs(p.X - sel.Left) <= edge) return CropHandle.Left;
        if (inY && Math.Abs(p.X - sel.Right) <= edge) return CropHandle.Right;
        if (inX && Math.Abs(p.Y - sel.Top) <= edge) return CropHandle.Top;
        if (inX && Math.Abs(p.Y - sel.Bottom) <= edge) return CropHandle.Bottom;
        return sel.Contains(p) ? CropHandle.Move : null;
    }

    static Cursor CursorFor(CropHandle? h) => h switch
    {
        CropHandle.Move => Cursors.SizeAll,
        CropHandle.Left or CropHandle.Right => Cursors.SizeWE,
        CropHandle.Top or CropHandle.Bottom => Cursors.SizeNS,
        CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
        CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Arrow,
    };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        if (HitTest(p) is not { } handle) return;
        _drag = (handle, p, _selection);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_drag is not { } d)
        {
            Cursor = CursorFor(HitTest(p));
            return;
        }
        var perPixel = ImageRect().Width / _sourceW; // DIP per original pixel, as shown right now
        var dx = (p.X - d.Start.X) / perPixel;
        var dy = (p.Y - d.Start.Y) / perPixel;
        var next = CropMath.Resize(d.From, d.Handle, dx, dy, Aspect, _sourceW, _sourceH);
        if (next == _selection) return;
        Selection = next;
        SelectionChanged?.Invoke(next);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag is null) return;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _drag = null;
    }

    static Rect Inflate(Rect r, double by)
    {
        r.Inflate(by, by);
        return r;
    }

    static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
