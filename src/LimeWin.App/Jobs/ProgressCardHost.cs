using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LimeWin.App.Jobs;

/// <summary>A borderless topmost window that stacks progress cards in the bottom-right of the work area of the monitor under the cursor.</summary>
public sealed class ProgressCardHost
{
    const double MarginDip = 12;

    readonly StackPanel _stack = new();
    readonly Window _window;
    Native.POINT _anchor; // a point on the monitor the stack lives on while visible

    public ProgressCardHost()
    {
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
            Content = _stack,
        };
        _window.SourceInitialized += (_, _) => Native.MakeNoActivate(_window);
        _window.SizeChanged += (_, _) => Reposition();
    }

    public ProgressCard Add(string title, Action cancel)
    {
        var card = new ProgressCard(title, cancel);
        card.CloseRequested += Remove;
        if (_stack.Children.Count == 0) _anchor = Native.CursorPos();
        _stack.Children.Add(card);
        if (!_window.IsVisible)
        {
            PlaceHidden(); // so it never flashes at its old or default spot
            _window.Show();
        }
        Reposition();
        return card;
    }

    /// <summary>Moves the hidden window to where the stack will end up, estimating its size at the target monitor's DPI.</summary>
    void PlaceHidden()
    {
        var work = Native.MonitorAt(_anchor.X, _anchor.Y).Work;
        var scale = Native.ScaleAt(_anchor.X, _anchor.Y);
        _stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = (int)Math.Ceiling(_stack.DesiredSize.Width * scale);
        var h = (int)Math.Ceiling(_stack.DesiredSize.Height * scale);
        var margin = (int)Math.Round(MarginDip * scale);
        Native.MoveTo(_window, work.Right - w - margin, work.Bottom - h - margin);
    }

    void Remove(ProgressCard card)
    {
        _stack.Children.Remove(card);
        if (_stack.Children.Count == 0) _window.Hide();
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
