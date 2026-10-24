using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Zest.Core.Catalog;
using Zest.Core.Wheel;

namespace Zest.App.Wheel;

/// <summary>
/// Owns the drop overlay and the small wheel window for one drag. When to close is decided by <see cref="OverlayCloseRules"/>
/// (tested in Core); this class only feeds it events, a monotonic clock and the polled button state, and acts on the answer.
/// </summary>
public sealed class WheelController
{
    static readonly TimeSpan SafetyInterval = TimeSpan.FromMilliseconds(100);

    readonly Capabilities _caps;
    readonly OverlayWindow _overlay = new();
    readonly WheelView _view = new();
    readonly Window _wheelWindow;
    readonly OverlayCloseRules _rules = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DispatcherTimer _safety, _dropFallback;

    WheelMode _mode;
    Point _anchor;
    IReadOnlyList<string>? _files;
    IReadOnlyList<WheelItem> _items = [];
    bool _fallbackArmed;
    bool _droppedOnPetal; // decides between an instant close (drop) and the cancel fade

    /// <summary>An item was dropped on: the item, the dropped files, and the drop point in physical screen pixels.</summary>
    public event Action<WheelItem, IReadOnlyList<string>, Point>? ItemDropped;

    public bool IsOpen { get; private set; }

    public WheelController(Capabilities caps)
    {
        _caps = caps;
        _wheelWindow = new Window
        {
            Title = "Zest Wheel",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            // The WPF-UI implicit Window style sets MinWidth/MinHeight; this window must be exactly the canvas.
            MinWidth = 0,
            MinHeight = 0,
            Width = WheelGeometry.CanvasSide,
            Height = WheelGeometry.CanvasSide,
            Content = _view,
        };
        // Drops and hit-testing belong to the overlay underneath, so the wheel window must be invisible to the mouse and OLE.
        _wheelWindow.SourceInitialized += (_, _) => Native.MakeClickThrough(_wheelWindow);

        _overlay.FilesEntered += OnFilesEntered;
        _overlay.NonFileDragEntered += () => Apply(_rules.NonFileDragEntered());
        _overlay.HoverChanged += _view.Highlight;
        _overlay.Dropped += OnDropped;

        _safety = new DispatcherTimer { Interval = SafetyInterval };
        _safety.Tick += (_, _) => Apply(_rules.SafetyTick(Native.PrimaryButtonDown(), _clock.Elapsed));
        // One-shot timer so the drop fallback fires on time instead of on the next 100 ms safety tick.
        _dropFallback = new DispatcherTimer { Interval = OverlayCloseRules.DropTimeout };
        _dropFallback.Tick += (_, _) =>
        {
            _dropFallback.Stop();
            Apply(_rules.SafetyTick(Native.PrimaryButtonDown(), _clock.Elapsed));
        };
    }

    /// <summary>Puts up the drop surface; the wheel itself is drawn only once a FileDrop DragEnter arrives.</summary>
    public void Show(WheelMode mode, Point screenPx)
    {
        if (IsOpen)
        {
            SwitchMode(mode);
            return;
        }
        IsOpen = true;
        _mode = mode;
        _anchor = screenPx;
        _files = null;
        _items = [];
        _rules.Reset();
        _fallbackArmed = false;
        _droppedOnPetal = false;
        HideWheel(); // a previous wheel may still be fading out
        _view.Palette = WheelStyle.SystemPalette();
        _overlay.ShowOverVirtualScreen();
        _safety.Start();
    }

    /// <summary>Another modifier was tapped: swap the petals in place, no transition.</summary>
    public void SwitchMode(WheelMode mode)
    {
        if (!IsOpen || mode == _mode) return;
        _mode = mode;
        if (_files is not null) DrawWheel();
    }

    /// <summary>Closes at once (app shutdown, wheel turned off, error recovery).</summary>
    public void Close() => Close(fade: false);

    /// <summary>
    /// The overlay always goes at once so input is never blocked; a cancelled wheel fades out (100 ms) instead of vanishing,
    /// while a drop on a petal closes instantly and lets the progress card take over.
    /// </summary>
    void Close(bool fade)
    {
        if (!IsOpen) return;
        IsOpen = false;
        _safety.Stop();
        _dropFallback.Stop();
        _files = null;
        _items = [];
        _overlay.Hide();
        if (fade && _wheelWindow.IsVisible) _view.FadeOut(HideWheel);
        else HideWheel();
    }

    void HideWheel()
    {
        _view.StopFade();
        _wheelWindow.Hide();
    }

    /// <summary>The hook saw the button go up.</summary>
    public void OnButtonReleased()
    {
        if (IsOpen) Apply(_rules.ButtonUp(_clock.Elapsed));
    }

    /// <summary>Esc was pressed.</summary>
    public void OnEscape()
    {
        if (IsOpen) Apply(_rules.Escape());
    }

    void Apply(OverlayAction action)
    {
        if (!IsOpen) return;
        switch (action)
        {
            case OverlayAction.Close:
                Close(fade: !_droppedOnPetal);
                break;
            case OverlayAction.AwaitDrop when !_fallbackArmed:
                _fallbackArmed = true; // once per drag; the 100 ms safety tick covers a timer that fires a hair early
                _dropFallback.Start();
                break;
        }
    }

    void OnFilesEntered(IReadOnlyList<string> files)
    {
        if (!IsOpen) return;
        _files = files;
        DrawWheel();
        Apply(_rules.FileDragEntered());
    }

    void DrawWheel()
    {
        var x = (int)_anchor.X;
        var y = (int)_anchor.Y;
        var showing = !_wheelWindow.IsVisible;
        if (showing)
        {
            // Place while hidden so it does not flash at its old spot, then grab the screen it will cover for the glass.
            // A mode switch keeps the first capture: the wheel itself is on screen by then.
            var area = Native.CenterOn(_wheelWindow, x, y);
            _view.SetBackdrop(BackdropCapture.Capture(area));
        }

        _items = WheelCatalog.Items(_files!, _mode, _caps);
        if (_items.Count == 0) _view.ShowEmpty();
        else _view.SetItems(_items);

        if (showing) _wheelWindow.Show();
        var rect = Native.CenterOn(_wheelWindow, x, y);
        var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        _overlay.SetWheel(center, rect.Width / WheelGeometry.CanvasSide, _items.Count);
    }

    void OnDropped(int? petal)
    {
        var item = petal is { } i && i < _items.Count ? _items[i] : null;
        var files = _files;
        _droppedOnPetal = item is not null;
        var at = Native.CursorPos();
        try
        {
            if (item is not null && files is not null) ItemDropped?.Invoke(item, files, new Point(at.X, at.Y));
        }
        finally
        {
            Apply(_rules.Dropped());
        }
    }
}
