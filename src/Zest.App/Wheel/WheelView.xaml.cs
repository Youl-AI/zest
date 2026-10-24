using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zest.Core.Catalog;
using G = Zest.Core.Wheel.WheelGeometry;
using S = Zest.App.Wheel.WheelStyle;

namespace Zest.App.Wheel;

/// <summary>
/// The glass wheel: blurred screen capture under a tint film, rounded petals with labels (and icons for tools and actions),
/// a lime highlight that lifts outward, and a round hub naming the highlighted item. Every value comes from <see cref="WheelStyle"/>.
/// </summary>
public partial class WheelView : UserControl
{
    sealed record PetalVisual(Path Glow, TranslateTransform Lift, SolidColorBrush Text, Vector LiftBy);

    readonly List<PetalVisual> _petals = [];
    IReadOnlyList<WheelItem> _items = [];
    int? _highlight;
    int _fadeGeneration;

    public WheelView()
    {
        InitializeComponent();
        Backdrop.Effect = new BlurEffect { Radius = S.BlurRadius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
        Hub.Width = Hub.Height = S.HubRadius * 2;
    }

    /// <summary>Colors used by the next <see cref="SetItems"/> or <see cref="ShowEmpty"/>.</summary>
    public WheelPalette Palette { get; set; } = S.LightPalette;

    /// <summary>False renders state changes instantly (the <c>--render</c> harness).</summary>
    public bool Animate { get; set; } = true;

    public int PetalCount => _items.Count;

    /// <summary>The screen behind the wheel (physical pixels of the wheel window's area), or null for an opaque disc.</summary>
    public void SetBackdrop(BitmapSource? image)
    {
        Backdrop.Source = image;
        Glass.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetItems(IReadOnlyList<WheelItem> items)
    {
        if (items.Count == 0)
        {
            ShowEmpty();
            return;
        }

        _items = items;
        _highlight = null;
        _petals.Clear();
        Petals.Children.Clear();
        SetSurface(new EllipseGeometry(PetalPath.Center, S.DiscRadius, S.DiscRadius),
            new EllipseGeometry(PetalPath.Center, S.DiscRadius - S.RingThickness / 2, S.DiscRadius - S.RingThickness / 2),
            new EllipseGeometry(PetalPath.Center, S.DiscRadius - S.RingThickness - S.InnerHighlightThickness / 2,
                S.DiscRadius - S.RingThickness - S.InnerHighlightThickness / 2));

        var petalFill = Frozen(Palette.Petal);
        var petalEdge = Frozen(Palette.PetalEdge);
        var glowFill = new LinearGradientBrush(Palette.HighlightTop, Palette.Highlight, new Point(0, 0), new Point(0, G.CanvasSide))
        {
            MappingMode = BrushMappingMode.Absolute,
        };
        glowFill.Freeze();
        var glowStroke = Frozen(Palette.HighlightStroke);

        for (var i = 0; i < items.Count; i++)
        {
            var outline = PetalPath.Outline(i, items.Count);
            var text = new SolidColorBrush(Palette.Label);
            var lift = new TranslateTransform();
            var group = new Canvas { RenderTransform = lift };
            group.Children.Add(new Path { Data = outline, Fill = petalFill, Stroke = petalEdge, StrokeThickness = S.PetalEdgeThickness });
            var glow = new Path { Data = outline, Fill = glowFill, Stroke = glowStroke, StrokeThickness = S.HighlightStrokeThickness, Opacity = 0 };
            group.Children.Add(glow);
            group.Children.Add(Label(items[i], i, items.Count, text));
            Petals.Children.Add(group);
            _petals.Add(new PetalVisual(glow, lift, text, PetalPath.LiftOf(i, items.Count)));
        }

        Hub.Fill = Frozen(Palette.Hub);
        Hub.Visibility = Visibility.Visible;
        SetCenterText("", null);
    }

    /// <summary>No common operation: a small glass pill with the note, no petals.</summary>
    public void ShowEmpty()
    {
        _items = [];
        _highlight = null;
        _petals.Clear();
        Petals.Children.Clear();
        Hub.Visibility = Visibility.Collapsed;

        CenterText.Text = S.NothingToDo;
        CenterText.FontFamily = S.KoreanFont;
        CenterText.FontSize = S.EmptySize;
        CenterText.FontWeight = S.LabelWeight;
        CenterText.Foreground = Frozen(Palette.Label);
        CenterText.Width = double.NaN;
        CenterText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var width = Math.Ceiling(CenterText.DesiredSize.Width) + 2 * S.EmptyPadding;
        var pill = new Rect(PetalPath.Center.X - width / 2, PetalPath.Center.Y - S.EmptyHeight / 2, width, S.EmptyHeight);
        Rect Inset(double by) { var r = pill; r.Inflate(-by, -by); return r; }
        SetSurface(new RectangleGeometry(pill, S.EmptyCorner, S.EmptyCorner),
            new RectangleGeometry(Inset(S.RingThickness / 2), S.EmptyCorner, S.EmptyCorner),
            new RectangleGeometry(Inset(S.RingThickness + S.InnerHighlightThickness / 2), S.EmptyCorner, S.EmptyCorner));
    }

    /// <summary>Highlights one petal (null clears) and names it in the hub.</summary>
    public void Highlight(int? index)
    {
        if (index is { } i && (i < 0 || i >= _petals.Count)) index = null;
        if (index == _highlight || _items.Count == 0) return;

        if (_highlight is { } old) Paint(_petals[old], false);
        _highlight = index;
        if (index is { } now) Paint(_petals[now], true);

        if (index is { } n) SetCenterText(_items[n].Label, _items[n].Operation);
        else SetCenterText("", null);
    }

    /// <summary>Cancel: fade the whole wheel out, then call <paramref name="done"/> unless <see cref="StopFade"/> came first.</summary>
    public void FadeOut(Action done)
    {
        var generation = ++_fadeGeneration;
        Motion.To(this, OpacityProperty, 0.0, S.CancelFade, S.EaseOut, () =>
        {
            if (generation == _fadeGeneration) done();
        });
    }

    /// <summary>Abandons a running fade and shows the wheel fully opaque again.</summary>
    public void StopFade()
    {
        _fadeGeneration++;
        Motion.Set(this, OpacityProperty, 1.0);
    }

    void Paint(PetalVisual petal, bool on)
    {
        var glow = on ? 1.0 : 0.0;
        var text = on ? S.HighlightText : Palette.Label;
        // Reduced motion keeps the color change but not the rise.
        var lift = on && !Motion.ReduceMotion ? petal.LiftBy : new Vector();
        if (!Animate)
        {
            Motion.Set(petal.Glow, OpacityProperty, glow);
            Motion.Set(petal.Text, SolidColorBrush.ColorProperty, text);
            Motion.Set(petal.Lift, TranslateTransform.XProperty, lift.X);
            Motion.Set(petal.Lift, TranslateTransform.YProperty, lift.Y);
            return;
        }
        Motion.To(petal.Glow, OpacityProperty, glow, S.HighlightColor, S.Ease);
        Motion.To(petal.Text, SolidColorBrush.ColorProperty, text, S.HighlightColor, S.Ease);
        Motion.To(petal.Lift, TranslateTransform.XProperty, lift.X, S.HighlightLift, S.EaseOut);
        Motion.To(petal.Lift, TranslateTransform.YProperty, lift.Y, S.HighlightLift, S.EaseOut);
    }

    /// <summary>Clips the blurred capture and shapes the tint and rim lines (disc or empty-state pill).</summary>
    void SetSurface(Geometry shape, Geometry ring, Geometry innerLine)
    {
        Glass.Clip = shape;
        Tint.Data = shape;
        Tint.Fill = Frozen(Backdrop.Source is null ? Palette.TintOpaque : Palette.Tint);
        Ring.Data = ring;
        Ring.Stroke = Frozen(Palette.Ring);
        Ring.StrokeThickness = S.RingThickness;
        InnerLine.Data = innerLine;
        InnerLine.Stroke = Frozen(Palette.InnerHighlight);
        InnerLine.StrokeThickness = S.InnerHighlightThickness;
    }

    void SetCenterText(string label, Operation? operation)
    {
        var format = operation is ConvertOp;
        CenterText.Text = format ? label.ToUpperInvariant() : label;
        CenterText.FontFamily = format ? S.FormatFont : S.KoreanFont;
        CenterText.FontSize = S.HubSize;
        CenterText.FontWeight = S.LabelWeight;
        CenterText.Foreground = Frozen(Palette.Label);
        CenterText.Width = S.HubTextWidth;
    }

    /// <summary>A petal's label (format petals: uppercase text; tools and actions: icon above Korean text), centred on the label radius.</summary>
    static FrameworkElement Label(WheelItem item, int index, int count, Brush text)
    {
        var maxWidth = count > 1 ? 2 * S.LabelRadius * Math.Sin(Math.PI / count) * S.LabelChordFraction : G.CanvasSide;
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        var icon = S.IconFor(item.Operation);
        if (icon is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = icon,
                FontFamily = S.IconFont,
                FontSize = S.IconSize,
                Foreground = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, S.IconGap),
            });
        }

        var format = item.Operation is ConvertOp;
        var label = new TextBlock
        {
            Text = format ? item.Label.ToUpperInvariant() : item.Label,
            FontFamily = format ? S.FormatFont : S.KoreanFont,
            FontSize = format ? S.FormatSize : S.KoreanSize,
            FontWeight = S.LabelWeight,
            Foreground = text,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        // Too wide for a narrow petal: break between words only (WPF would otherwise split Hangul words mid-syllable run).
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (label.DesiredSize.Width > maxWidth) label.Text = label.Text.Replace(' ', '\n');
        stack.Children.Add(label);

        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var at = PetalPath.At(PetalPath.Bearing(index, count), S.LabelRadius);
        Canvas.SetLeft(stack, at.X - stack.DesiredSize.Width / 2);
        Canvas.SetTop(stack, at.Y - stack.DesiredSize.Height / 2);
        return stack;
    }

    static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
