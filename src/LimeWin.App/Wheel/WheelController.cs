using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LimeWin.Core.Catalog;
using LimeWin.Core.Wheel;

namespace LimeWin.App.Wheel;

/// <summary>
/// Owns the drop overlay and the small wheel window for one drag.
/// Close rules (docs/spike-notes.md): the hook's button-up usually arrives before OLE's Drop, so once the overlay has seen a
/// FileDrop the button-up only starts "awaiting drop" — Drop closes, or a 500 ms fallback does. Without a FileDrop it closes at once.
/// A 100 ms GetAsyncKeyState timer backs up a missed button-up and follows the same rule. Esc closes immediately.
/// </summary>
public sealed class WheelController
{
    static readonly TimeSpan SafetyInterval = TimeSpan.FromMilliseconds(100);
    static readonly TimeSpan AwaitDropTimeout = TimeSpan.FromMilliseconds(500);

    readonly Capabilities _caps;
    readonly OverlayWindow _overlay = new();
    readonly WheelView _view = new();
    readonly Window _wheelWindow;
    readonly DispatcherTimer _safety, _awaitDrop;

    WheelMode _mode;
    Point _anchor;
    IReadOnlyList<string>? _files;
    IReadOnlyList<WheelItem> _items = [];
    bool _awaitingDrop;

    /// <summary>An item was dropped on: the item, the dropped files, and the drop point in physical screen pixels.</summary>
    public event Action<WheelItem, IReadOnlyList<string>, Point>? ItemDropped;

    public bool IsOpen { get; private set; }

    public WheelController(Capabilities caps)
    {
        _caps = caps;
        _wheelWindow = new Window
        {
            Title = "Lime Wheel",
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
        _overlay.HoverChanged += _view.Highlight;
        _overlay.Dropped += OnDropped;

        _safety = new DispatcherTimer { Interval = SafetyInterval };
        _safety.Tick += (_, _) => OnSafetyTick();
        _awaitDrop = new DispatcherTimer { Interval = AwaitDropTimeout };
        _awaitDrop.Tick += (_, _) => Close();
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
        _awaitingDrop = false;
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

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _safety.Stop();
        _awaitDrop.Stop();
        _awaitingDrop = false;
        _files = null;
        _items = [];
        _wheelWindow.Hide();
        _overlay.Hide();
    }

    /// <summary>The hook saw the button go up.</summary>
    public void OnButtonReleased()
    {
        if (!IsOpen) return;
        if (_overlay.ReceivedFiles) BeginAwaitingDrop();
        else Close();
    }

    void BeginAwaitingDrop()
    {
        if (_awaitingDrop) return;
        _awaitingDrop = true;
        _awaitDrop.Start();
    }

    void OnSafetyTick()
    {
        if (!Native.PrimaryButtonDown()) OnButtonReleased();
    }

    void OnFilesEntered(IReadOnlyList<string> files)
    {
        if (!IsOpen) return;
        _files = files;
        DrawWheel();
    }

    void DrawWheel()
    {
        _items = WheelCatalog.Items(_files!, _mode, _caps);
        if (_items.Count == 0) _view.ShowEmpty();
        else _view.SetItems(_items);

        var x = (int)_anchor.X;
        var y = (int)_anchor.Y;
        if (!_wheelWindow.IsVisible)
        {
            Native.CenterOn(_wheelWindow, x, y); // place while hidden so it does not flash at its old spot
            _wheelWindow.Show();
        }
        var rect = Native.CenterOn(_wheelWindow, x, y);
        var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        _overlay.SetWheel(center, rect.Width / WheelGeometry.CanvasSide, _items.Count);
    }

    void OnDropped(int? petal)
    {
        var item = petal is { } i && i < _items.Count ? _items[i] : null;
        var files = _files;
        var at = Native.CursorPos();
        try
        {
            if (item is not null && files is not null) ItemDropped?.Invoke(item, files, new Point(at.X, at.Y));
        }
        finally
        {
            Close();
        }
    }
}
