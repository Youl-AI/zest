using System.IO;
using System.Windows;
using System.Windows.Interop;
using LimeWin.Core.Input;
using LimeWin.Core.Wheel;

namespace LimeWin.App.Wheel;

/// <summary>
/// Near-invisible drop surface over the whole virtual screen. It is the only OLE drop target while the wheel is up:
/// it receives the dragged file list and hit-tests petals against the wheel placed by <see cref="SetWheel"/>.
/// </summary>
public partial class OverlayWindow : Window
{
    readonly WheelGeometry _geometry = new();
    Point _centerPx;
    double _scale = 1;
    int _petalCount;
    int? _hover;

    /// <summary>A drag carrying a FileDrop entered; the controller should call <see cref="SetWheel"/> before returning.</summary>
    public event Action<IReadOnlyList<string>>? FilesEntered;

    /// <summary>A drag without FileDrop entered (text, virtual shell items); the overlay must not keep it.</summary>
    public event Action? NonFileDragEntered;

    /// <summary>The petal under the cursor changed (null = hole, gutter or outside).</summary>
    public event Action<int?>? HoverChanged;

    /// <summary>The drag was dropped; the argument is the petal under the cursor, if any.</summary>
    public event Action<int?>? Dropped;

    /// <summary>True once a FileDrop DragEnter arrived since the last <see cref="ShowOverVirtualScreen"/>.</summary>
    public bool ReceivedFiles { get; private set; }

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(Native.Handle(this))!.AddHook(KeepPhysicalBounds);
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, e) => { SetHover(null); e.Handled = true; };
        Drop += OnDrop;
    }

    /// <summary>Shows the surface over every monitor (physical pixels, negative origins included).</summary>
    public void ShowOverVirtualScreen()
    {
        ReceivedFiles = false;
        _petalCount = 0;
        _hover = null;
        var bounds = Native.VirtualScreen();
        Native.SetBounds(this, bounds);
        Show();
        Native.SetBounds(this, bounds);
    }

    /// <summary>Where the wheel is drawn: centre in physical pixels, physical pixels per canvas unit, and petal count (0 = none).</summary>
    public void SetWheel(Point centerPx, double scale, int petalCount)
    {
        _centerPx = centerPx;
        _scale = scale;
        _petalCount = petalCount;
        _hover = null; // the view was just redrawn without a highlight; the next DragOver re-reports the petal
    }

    void OnDragEnter(object sender, DragEventArgs e)
    {
        // Copy is the only effect Lime ever answers; a source that does not offer it (move-only) is refused.
        if (e.AllowedEffects.HasFlag(DragDropEffects.Copy)
            && e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0
            && DropSourcePolicy.IsRealUserFiles(files, Path.GetTempPath()))
        {
            ReceivedFiles = true;
            FilesEntered?.Invoke(files);
            OnDragOver(sender, e);
            return;
        }
        // Move-only sources, text, virtual items (zip contents, MTP), files an archiver extracted into %TEMP% (Bandizip deletes the archive entry
        // when a drop with Shift is accepted) and the like are not ours: refuse, and let the controller close the
        // overlay so OLE re-targets the window underneath on its next poll.
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        NonFileDragEntered?.Invoke();
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        SetHover(HitTest());
        e.Effects = _hover is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        // HitTest is null unless DragEnter accepted the files (ReceivedFiles), so a refused drag ends as Dropped(null): None + close.
        var petal = HitTest();
        // Copy tells the source we used the data; never Move, which would make Explorer delete the originals.
        e.Effects = petal is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
        Dropped?.Invoke(petal);
    }

    int? HitTest()
    {
        if (!ReceivedFiles || _petalCount == 0) return null;
        var cursor = Native.CursorPos();
        return _geometry.PetalAt((cursor.X - _centerPx.X) / _scale, (cursor.Y - _centerPx.Y) / _scale, _petalCount);
    }

    void SetHover(int? petal)
    {
        if (petal == _hover) return;
        _hover = petal;
        HoverChanged?.Invoke(petal);
    }

    // The surface spans monitors of possibly different DPI; WPF must not rescale it to one monitor's suggested rect.
    static IntPtr KeepPhysicalBounds(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_DPICHANGED) handled = true;
        return IntPtr.Zero;
    }
}
