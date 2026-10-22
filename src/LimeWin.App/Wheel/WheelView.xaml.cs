using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LimeWin.Core.Catalog;
using G = LimeWin.Core.Wheel.WheelGeometry;

namespace LimeWin.App.Wheel;

/// <summary>Plain functional wheel: petals, labels, highlight and centre text. Visual styling is applied later (Task 6B).</summary>
public partial class WheelView : UserControl
{
    static readonly Brush PetalFill = Frozen(Color.FromArgb(0xE6, 0xF5, 0xF5, 0xF5));
    static readonly Brush PetalStroke = Frozen(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
    static readonly Brush HighlightFill = Frozen(Color.FromRgb(0x8B, 0xC3, 0x4A));
    static readonly Brush LabelText = Frozen(Color.FromRgb(0x1A, 0x1A, 0x1A));
    static readonly Brush HighlightText = Brushes.White;

    const string NothingToDo = "할 수 있는 작업 없음";

    readonly List<(Path Petal, TextBlock Label)> _petals = [];
    IReadOnlyList<WheelItem> _items = [];
    int? _highlight;

    public WheelView() => InitializeComponent();

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
        for (var i = 0; i < items.Count; i++)
        {
            var petal = new Path { Data = PetalPath.Outline(i, items.Count), Fill = PetalFill, Stroke = PetalStroke, StrokeThickness = 1 };
            var label = new TextBlock { Text = items[i].Label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = LabelText };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var at = PetalPath.At(PetalPath.Bearing(i, items.Count), (G.Inner + G.Outer) / 2);
            Canvas.SetLeft(label, at.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, at.Y - label.DesiredSize.Height / 2);
            Petals.Children.Add(petal);
            Petals.Children.Add(label);
            _petals.Add((petal, label));
        }
        Hole.Visibility = Visibility.Collapsed;
        CenterText.Text = "";
    }

    /// <summary>No common operation: only the centre message is shown.</summary>
    public void ShowEmpty()
    {
        _items = [];
        _highlight = null;
        _petals.Clear();
        Petals.Children.Clear();
        Hole.Visibility = Visibility.Visible;
        CenterText.Text = NothingToDo;
    }

    /// <summary>Highlights one petal (null clears) and shows its label in the hole.</summary>
    public void Highlight(int? index)
    {
        if (index is { } i && (i < 0 || i >= _petals.Count)) index = null;
        if (index == _highlight || _items.Count == 0) return;

        if (_highlight is { } old) Paint(old, false);
        _highlight = index;
        if (index is { } now) Paint(now, true);

        Hole.Visibility = index is null ? Visibility.Collapsed : Visibility.Visible;
        CenterText.Text = index is { } n ? _items[n].Label : "";
    }

    void Paint(int index, bool on)
    {
        var (petal, label) = _petals[index];
        petal.Fill = on ? HighlightFill : PetalFill;
        label.Foreground = on ? HighlightText : LabelText;
    }

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
