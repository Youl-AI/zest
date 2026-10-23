using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LimeWin.App.Jobs;

/// <summary>
/// A borderless topmost window that stacks progress cards in the bottom-right of the work area of the monitor under the cursor.
/// At most <see cref="MaxVisibleCards"/> cards show at once; later ones wait in order behind one "외 N개 작업" summary card
/// (with "모두 취소" for them) and move up as visible cards close.
/// </summary>
public sealed class ProgressCardHost
{
    // Each card carries 12 DIP of transparent padding for its shadow, which also keeps it 12 DIP off the work-area edge.
    const double MarginDip = 0;
    const int MaxVisibleCards = 6;

    readonly StackPanel _root = new() { Margin = new Thickness(0, 12, 0, 0) }; // shadow room above the top card
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
            Title = "Lime Progress",
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

    public ProgressCard Add(string title, Action cancel)
    {
        var card = new ProgressCard(title, cancel);
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

    /// <summary>The "외 N개 작업" card, styled like a progress card (same width, surface, stroke and shadow).</summary>
    static (FrameworkElement Card, TextBlock Text) BuildSummary(Action cancelAll)
    {
        var text = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var button = new Wpf.Ui.Controls.Button
        {
            Content = "모두 취소",
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(10, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Arrow,
        };
        button.Click += (_, _) => cancelAll();
        var row = new DockPanel();
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        row.Children.Add(text);
        var surface = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 12, 10),
            Child = row,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.16, Color = Colors.Black },
        };
        surface.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorFlyoutBrush");
        TextOptions.SetTextFormattingMode(surface, TextFormattingMode.Display);
        var card = new Border { Width = 344, Padding = new Thickness(12, 0, 12, 12), Child = surface, Visibility = Visibility.Collapsed };
        return (card, text);
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
