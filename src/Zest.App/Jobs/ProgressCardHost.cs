using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zest.App.Theme;
using Zest.App.Wheel;
using Zest.Core.Jobs;

namespace Zest.App.Jobs;

/// <summary>
/// A borderless topmost window that stacks progress cards in the bottom-right of the work area of the monitor under the cursor.
/// At most <see cref="MaxVisibleCards"/> cards show at once; later ones wait in order behind one "외 N개 작업" summary card
/// (with "모두 취소" for them) and move up as visible cards close.
/// </summary>
public sealed class ProgressCardHost
{
    // Each card carries transparent padding for its shadow (ProgressCard.ShadowRoom), which also keeps it off the work-area edge.
    const double MarginDip = 0;
    const int MaxVisibleCards = 6;

    readonly StackPanel _root = new() { Margin = new Thickness(0, 14, 0, 4) }; // shadow room above the top card and under the last
    readonly StackPanel _stack = new();
    readonly List<ProgressCard> _overflow = []; // added beyond the cap, oldest first
    readonly FrameworkElement _summary;
    readonly TextBlock _summaryText;
    readonly Window _window;
    Native.POINT _anchor; // a point on the monitor the stack lives on while visible

    public ProgressCardHost()
    {
        (_summary, _summaryText) = BuildSummary(() =>
        {
            foreach (var card in _overflow.ToList()) card.RequestCancel();
        });
        _root.Children.Add(_stack);
        _root.Children.Add(_summary);
        _window = new Window
        {
            Title = "Zest Progress",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 0, // override the WPF-UI implicit Window style's minimum size
            MinHeight = 0,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = _root,
        };
        _window.SourceInitialized += (_, _) => Native.MakeNoActivate(_window);
        _window.SizeChanged += (_, _) => Reposition();
    }

    public ProgressCard Add(string title, string detail, JobKind kind, Action cancel)
    {
        var card = new ProgressCard(title, detail, kind, cancel);
        card.CloseRequested += Remove;
        if (_stack.Children.Count >= MaxVisibleCards)
        {
            _overflow.Add(card); // runs (and can finish) unseen until a slot frees up
            UpdateSummary();
        }
        else Show(card);
        return card;
    }

    void Show(ProgressCard card)
    {
        if (!_window.IsVisible) _anchor = Native.CursorPos();
        _stack.Children.Add(card);
        if (!_window.IsVisible)
        {
            PlaceHidden(); // so it never flashes at its old or default spot
            _window.Show();
        }
        Reposition();
        card.AnimateIn();
    }

    /// <summary>Moves the hidden window to where the stack will end up, estimating its size at the target monitor's DPI.</summary>
    void PlaceHidden()
    {
        var work = Native.MonitorAt(_anchor.X, _anchor.Y).Work;
        var scale = Native.ScaleAt(_anchor.X, _anchor.Y);
        _root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = (int)Math.Ceiling(_root.DesiredSize.Width * scale);
        var h = (int)Math.Ceiling(_root.DesiredSize.Height * scale);
        var margin = (int)Math.Round(MarginDip * scale);
        Native.MoveTo(_window, work.Right - w - margin, work.Bottom - h - margin);
    }

    void Remove(ProgressCard card)
    {
        if (_overflow.Remove(card))
        {
            UpdateSummary(); // finished while waiting for a slot: it simply leaves the count
            return;
        }
        card.AnimateOut(() =>
        {
            _stack.Children.Remove(card);
            while (_stack.Children.Count < MaxVisibleCards && _overflow.Count > 0)
            {
                var next = _overflow[0];
                _overflow.RemoveAt(0);
                Show(next);
            }
            UpdateSummary();
            if (_stack.Children.Count == 0) _window.Hide();
        });
    }

    void UpdateSummary()
    {
        _summary.Visibility = _overflow.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _summaryText.Text = $"외 {_overflow.Count}개 작업";
    }

    /// <summary>
    /// The "외 N개 작업" card, built like a progress card (same width, radius 12, window colour, line, shadow layer and
    /// type): the count over "위 카드가 닫히면 차례로 올라옵니다", and a ghost "모두 취소" for the waiting jobs.
    /// </summary>
    internal static (FrameworkElement Card, TextBlock Text) BuildSummary(Action cancelAll)
    {
        var title = new TextBlock
        {
            FontFamily = WheelStyle.ChipFont,
            FontSize = 13.5,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(title, FontNumeralAlignment.Tabular);
        title.SetResourceReference(TextBlock.ForegroundProperty, "Zest.Text");
        var detail = new TextBlock
        {
            Text = "위 카드가 닫히면 차례로 올라옵니다",
            Margin = new Thickness(0, 1, 0, 0),
            FontFamily = WheelStyle.TextFont,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Zest.Text2");
        var text = new StackPanel { MinHeight = 32, VerticalAlignment = VerticalAlignment.Center, Children = { title, detail } };

        var button = new Button
        {
            Content = "모두 취소",
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Arrow,
            Focusable = false,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Zest.GhostButton");
        button.Click += (_, _) => cancelAll();

        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        Grid.SetColumn(button, 1);
        row.Children.Add(text);
        row.Children.Add(button);

        var shadow = new Border { CornerRadius = new CornerRadius(12) };
        shadow.SetResourceReference(Border.BackgroundProperty, "Zest.Window");
        CardShadow.Attach(shadow);
        var surface = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 12, 12),
            Child = row,
        };
        surface.SetResourceReference(Border.BackgroundProperty, "Zest.Window");
        surface.SetResourceReference(Border.BorderBrushProperty, "Zest.Line");
        var layers = new Grid { Children = { shadow, surface } };
        TextOptions.SetTextFormattingMode(layers, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(layers, TextRenderingMode.Grayscale);
        var card = new Border { Width = ProgressCard.OuterWidth, Padding = ProgressCard.ShadowRoom, Child = layers, Visibility = Visibility.Collapsed };
        return (card, title);
    }

    void Reposition()
    {
        if (!_window.IsVisible || _window.ActualWidth <= 0) return;
        var work = Native.MonitorAt(_anchor.X, _anchor.Y).Work;
        // Two passes: the first move may land on a monitor with another DPI and change the physical size.
        for (var pass = 0; pass < 2; pass++)
        {
            var r = Native.WindowRect(_window);
            var margin = (int)Math.Round(MarginDip * r.Width / _window.ActualWidth);
            Native.MoveTo(_window, work.Right - r.Width - margin, work.Bottom - r.Height - margin);
        }
    }
}
