using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Zest.Core.Catalog;
using Zest.Core.Design;
using G = Zest.Core.Wheel.WheelGeometry;
using S = Zest.App.Wheel.WheelStyle;

namespace Zest.App.Wheel;

/// <summary>
/// The wheel: rounded petals floating over the screen (no disc behind them), each with a 1 px edge in the mode's citrus and
/// one shared drop shadow, labels (and icons for tools and actions), a citrus highlight that lifts outward, and a round hub
/// naming the highlighted item above a mode chip. Every value comes from <see cref="WheelStyle"/>.
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
        Hub.Width = Hub.Height = S.HubRadius * 2;
        Chip.Padding = S.ChipPadding;
        Chip.Margin = new Thickness(0, S.ChipGap, 0, 0);
        ChipText.FontFamily = S.ChipFont;
        ChipText.FontSize = S.ChipSize;
        ChipText.FontWeight = S.FormatWeight;
        EmptyPill.Height = S.EmptyHeight;
        EmptyPill.Padding = new Thickness(S.EmptyPadding, 0, S.EmptyPadding, 0);
        EmptyPill.CornerRadius = new CornerRadius(S.EmptyCorner);
        EmptyPill.BorderThickness = new Thickness(S.PetalEdgeThickness);
        EmptyText.FontFamily = S.KoreanFont;
        EmptyText.FontSize = S.EmptySize;
        EmptyText.FontWeight = S.LabelWeight;
        EmptyText.Text = S.NothingToDo;
    }

    /// <summary>Theme colors used by the next <see cref="SetItems"/> or <see cref="ShowEmpty"/>.</summary>
    public WheelPalette Palette { get; set; } = S.LightPalette;

    /// <summary>Which wheel this is (picks the citrus and the hub chip); set before <see cref="SetItems"/>.</summary>
    public WheelMode Mode { get; set; } = WheelMode.Convert;

    /// <summary>False renders state changes instantly (the <c>--render</c> harness).</summary>
    public bool Animate { get; set; } = true;

    public int PetalCount => _items.Count;

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
        EmptyPill.Visibility = Visibility.Collapsed;

        var citrus = S.Accent(Mode);
        var edge = Frozen(S.PetalEdge(citrus, Palette.Dark));
        var petalFill = Frozen(Palette.Petal);
        var selectedFill = Frozen(S.ToColor(citrus.Fill));
        Petals.Effect = S.Shadow(Palette);

        for (var i = 0; i < items.Count; i++)
        {
            var outline = PetalPath.Outline(i, items.Count);
            var text = new SolidColorBrush(Palette.Label);
            var lift = new TranslateTransform();
            var group = new Canvas { RenderTransform = lift };
            group.Children.Add(new Path { Data = outline, Fill = petalFill, Stroke = edge, StrokeThickness = S.PetalEdgeThickness });
            // The selected fill covers the edge too, so a highlighted petal is one solid citrus shape.
            var glow = new Path { Data = outline, Fill = selectedFill, Stroke = selectedFill, StrokeThickness = S.PetalEdgeThickness, Opacity = 0 };
            group.Children.Add(glow);
            group.Children.Add(Label(items[i], i, items.Count, text));
            Petals.Children.Add(group);
            _petals.Add(new PetalVisual(glow, lift, text, PetalPath.LiftOf(i, items.Count)));
        }

        Hub.Fill = Frozen(Palette.Hub);
        Hub.Stroke = edge;
        Hub.StrokeThickness = S.PetalEdgeThickness;
        Hub.Effect = S.Shadow(Palette);
        Hub.Visibility = Visibility.Visible;
        Chip.CornerRadius = new CornerRadius(S.HubRadius); // fully round at any text width
        Chip.Background = Frozen(S.ToColor(citrus.Wash(Palette.Dark)));
        ChipText.Foreground = Frozen(S.ToColor(citrus.Ink(Palette.Dark)));
        ChipText.Text = S.ModeChip(Mode);
        HubContent.Visibility = Visibility.Visible;
        SetCenterText(null);
    }

    /// <summary>No common operation: a small pill in the window color with the note, no petals.</summary>
    public void ShowEmpty()
    {
        _items = [];
        _highlight = null;
        _petals.Clear();
        Petals.Children.Clear();
        Hub.Visibility = Visibility.Collapsed;
        HubContent.Visibility = Visibility.Collapsed;

        var surface = ZestColors.For(Palette.Dark);
        EmptyPill.Background = Frozen(S.ToColor(surface.Window));
        EmptyPill.BorderBrush = Frozen(S.ToColor(surface.Line));
        EmptyPill.Effect = S.Shadow(Palette);
        EmptyText.Foreground = Frozen(S.ToColor(surface.Text2));
        EmptyPill.Visibility = Visibility.Visible;
    }

    /// <summary>Highlights one petal (null clears) and names it in the hub.</summary>
    public void Highlight(int? index)
    {
        if (index is { } i && (i < 0 || i >= _petals.Count)) index = null;
        if (index == _highlight || _items.Count == 0) return;

        if (_highlight is { } old) Paint(_petals[old], false);
        _highlight = index;
        if (index is { } now) Paint(_petals[now], true);

        SetCenterText(index is { } n ? _items[n] : null);
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
        var text = on ? S.ToColor(S.Accent(Mode).OnFill) : Palette.Label;
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

    /// <summary>
    /// The hovered item's name above the chip. With nothing hovered the line keeps its height (hidden), so the chip does not
    /// jump when a name appears; the font follows the mode then, since a wheel shows either format names or Korean names.
    /// </summary>
    void SetCenterText(WheelItem? item)
    {
        var format = item is null ? Mode == WheelMode.Convert : item.Operation is ConvertOp;
        CenterText.Text = item is null ? " " : format ? item.Label.ToUpperInvariant() : item.Label;
        CenterText.FontFamily = format ? S.FormatFont : S.KoreanFont;
        CenterText.FontSize = format ? S.HubFormatSize : S.HubKoreanSize;
        CenterText.FontWeight = format ? S.FormatWeight : S.LabelWeight;
        CenterText.Foreground = Frozen(Palette.HubText);
        CenterText.Visibility = item is null ? Visibility.Hidden : Visibility.Visible;
        // A long name ("PDF 만들기") shrinks to the hub's width instead of being cut off.
        CenterText.MaxWidth = double.PositiveInfinity;
        CenterText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (CenterText.DesiredSize.Width > S.HubTextWidth) CenterText.FontSize *= S.HubTextWidth / CenterText.DesiredSize.Width;
        CenterText.MaxWidth = S.HubTextWidth;
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
            FontWeight = format ? S.FormatWeight : S.LabelWeight,
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
