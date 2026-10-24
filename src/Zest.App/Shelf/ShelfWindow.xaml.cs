using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;
using Zest.App.Theme;
using Zest.Core.Input;
using Zest.Core.Output;

namespace Zest.App.Shelf;

/// <summary>One file or folder on a shelf.</summary>
public sealed class ShelfItem(string path, ImageSource? icon, ImageSource? largeIcon, bool missing) : INotifyPropertyChanged
{
    bool _missing = missing;
    ImageSource? _icon = icon, _largeIcon = largeIcon;

    public string Path { get; } = path;
    public string Name { get; } = OutputNaming.DisplayName(path);

    /// <summary>The list row's small icon.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    /// <summary>The grid tile's large icon.</summary>
    public ImageSource? LargeIcon
    {
        get => _largeIcon;
        set
        {
            if (ReferenceEquals(_largeIcon, value)) return;
            _largeIcon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LargeIcon)));
        }
    }

    /// <summary>No longer at <see cref="Path"/> (moved or deleted elsewhere): shown dimmed and left out of drags.</summary>
    public bool Missing
    {
        get => _missing;
        set
        {
            if (_missing == value) return;
            _missing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Missing)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// A row shown at once without touching the disk (a slow or offline network path must never block a drop): the generic
    /// icon for the name's type. The real icon and whether it exists follow from <see cref="ShellIcon.LoadLater"/>.
    /// </summary>
    public static ShelfItem Placeholder(string path) => ForName(path, isFolder: !System.IO.Path.HasExtension(path));

    /// <summary>An item with the generic icons for its name's type (also the render harness's samples).</summary>
    internal static ShelfItem ForName(string path, bool isFolder) =>
        new(path, ShellIcon.ForName(path, isFolder), ShellIcon.ForName(path, isFolder, large: true), missing: false);

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}

/// <summary>
/// A small floating card that holds files (original <c>Shelf.swift</c>): a grid of icon tiles, or a list of rows (the
/// header's view button; the choice holds for the rest of the session). Drag the header to move it, × or Esc closes it
/// (the files stay where they are). Items drag out as a file drop (copy or move, the target decides), files dragged in
/// are added once each, Delete takes the selected items off the shelf. Topmost, opens without taking focus. Lime throughout.
/// </summary>
public partial class ShelfWindow : Window
{
    const string ListGlyph = "", GridGlyph = ""; // BulletedList, GridView: the view the button switches to
    static bool _listView; // grid first; the last choice holds for this app session

    readonly ObservableCollection<ShelfItem> _items = [];
    Point _pressAt;
    ListBoxItem? _pressed;
    bool _keepSelectionOnPress; // pressed on an already selected item: a drag takes the whole selection
    bool _draggingOut;

    public ShelfWindow(IReadOnlyList<string> paths)
        : this(paths.Select(ShelfItem.Placeholder), _listView)
    {
        foreach (var item in _items) LoadDetails(item);
    }

    /// <param name="listView">Grid or list; the render harness draws both, the app passes the session's choice.</param>
    internal ShelfWindow(IEnumerable<ShelfItem> items, bool listView)
    {
        InitializeComponent();
        CardShadow.Attach(ShadowLayer);
        List.ItemsSource = _items;
        foreach (var item in items) AddUnique(item);
        UpdateHeader();
        ShowView(listView);
        ViewButton.Click += (_, _) =>
        {
            _listView = !IsListView;
            ShowView(_listView);
        };

        SourceInitialized += (_, _) => Native.MakeToolWindow(this);
        // Rows added near the bottom edge grow the window (SizeToContent) past the work area; pull it back in.
        SizeChanged += (_, _) =>
        {
            if (IsVisible) Native.KeepInWorkArea(this);
        };
        Header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        CloseButton.Click += (_, _) => Close();
        PreviewKeyDown += OnKey;
        Activated += (_, _) => RefreshMissing();
        Card.MouseEnter += (_, _) => RefreshMissing();

        List.PreviewMouseLeftButtonDown += OnPress;
        List.PreviewMouseMove += OnPointerMove;
        List.PreviewMouseLeftButtonUp += OnRelease;

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
    }

    /// <summary>Selects items for the render harness.</summary>
    internal void Select(params int[] indexes)
    {
        foreach (var i in indexes) List.SelectedItems.Add(_items[i]);
    }

    /// <summary>
    /// Shows the shelf centred on a physical screen point, kept inside that monitor's work area, without activating it.
    /// Placed before it is shown (height measured first, as SizeToContent only applies during Show), so it never flashes elsewhere.
    /// </summary>
    public void ShowAt(int x, int y)
    {
        if (Content is FrameworkElement content)
        {
            content.Measure(new Size(Width, double.PositiveInfinity));
            Height = Math.Ceiling(content.DesiredSize.Height);
        }
        Native.PlaceAround(this, x, y);
        Show();
    }

    public void Add(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var item = ShelfItem.Placeholder(path);
            if (AddUnique(item)) LoadDetails(item);
        }
        UpdateHeader();
    }

    bool AddUnique(ShelfItem item)
    {
        if (_items.Any(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase))) return false;
        _items.Add(item);
        return true;
    }

    /// <summary>Fills in a placeholder's real icons and existence once the background reader has them.</summary>
    void LoadDetails(ShelfItem item) => ShellIcon.LoadLater(item.Path, Dispatcher, (icon, large, exists) =>
    {
        if (icon is not null) item.Icon = icon;
        if (large is not null) item.LargeIcon = large;
        item.Missing = !exists;
    });

    void UpdateHeader() => TitleText.Text = $"파일 {_items.Count}개";

    bool IsListView => ReferenceEquals(List.ItemTemplate, Resources["ListRow"]);

    /// <summary>
    /// Grid: three columns of tiles. List: one row per item. Same ListBox and selection either way; the view button shows
    /// (and names) the view it switches to.
    /// </summary>
    void ShowView(bool list)
    {
        List.ItemsPanel = (ItemsPanelTemplate)Resources[list ? "ListPanel" : "GridPanel"];
        List.ItemContainerStyle = (Style)Resources[list ? "ListItem" : "GridItem"];
        List.ItemTemplate = (DataTemplate)Resources[list ? "ListRow" : "GridTile"];
        List.Margin = list ? new Thickness(4) : new Thickness(6, 4, 6, 8);
        ViewButton.Content = list ? GridGlyph : ListGlyph;
        var name = list ? "격자로 보기" : "목록으로 보기";
        ViewButton.ToolTip = name;
        AutomationProperties.SetName(ViewButton, name);
    }

    /// <summary>Re-checks which rows still exist where they were.</summary>
    public void RefreshMissing()
    {
        foreach (var item in _items) item.Missing = !ShelfItem.Exists(item.Path);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.Delete when List.SelectedItems.Count > 0:
                e.Handled = true;
                // Takes rows off the shelf only; the files themselves are never touched.
                foreach (var item in List.SelectedItems.Cast<ShelfItem>().ToList()) _items.Remove(item);
                if (_items.Count == 0) Close();
                else UpdateHeader();
                break;
        }
    }

    // ── Drag out ────────────────────────────────────────────────────────────

    void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressAt = e.GetPosition(List);
        _pressed = ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) as ListBoxItem;
        // WPF would collapse a multi-selection to the pressed row on mouse down; keep it so the drag takes all of it,
        // and collapse on release instead if no drag happened.
        _keepSelectionOnPress = _pressed is { IsSelected: true } && List.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None;
        if (_keepSelectionOnPress) e.Handled = true;
    }

    void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;
        var moved = e.GetPosition(List) - _pressAt;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _pressed = null;
        _keepSelectionOnPress = false;
        DragOut();
    }

    void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (_keepSelectionOnPress && _pressed?.DataContext is ShelfItem item)
        {
            List.SelectedItems.Clear();
            List.SelectedItem = item;
        }
        _pressed = null;
        _keepSelectionOnPress = false;
    }

    void DragOut()
    {
        RefreshMissing();
        var paths = _items.Where(i => List.SelectedItems.Contains(i) && !i.Missing).Select(i => i.Path).ToArray();
        if (paths.Length == 0) return;
        var data = new DataObject(DataFormats.FileDrop, paths);
        _draggingOut = true;
        try
        {
            DragDrop.DoDragDrop(List, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            _draggingOut = false;
            // Explorer often reports an optimized move as "none", so check every row rather than trusting the effect.
            RefreshMissing();
        }
    }

    // ── Drag in ─────────────────────────────────────────────────────────────

    // Decided once per drag (the policy touches the file system; DragOver fires at mouse-move rate).
    bool _acceptDrag, _dragDecided;

    void OnDragEnter(object sender, DragEventArgs e)
    {
        // DragEnter bubbles on every child crossing; the decision holds for the whole drag.
        if (_dragDecided)
        {
            OnDragOver(sender, e);
            return;
        }
        _dragDecided = true;
        // Archive-extracted temp copies vanish with the archiver's clean-up; never shelve them. A source that does not offer
        // Copy (the only effect a shelf answers) is refused too.
        _acceptDrag = !_draggingOut && e.AllowedEffects.HasFlag(DragDropEffects.Copy) && e.Data.GetData(DataFormats.FileDrop) is string[] files
            && DropSourcePolicy.IsRealUserFiles(files, Path.GetTempPath());
        OnDragOver(sender, e);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        // Copy, never Move: the source must not delete anything because it landed on a shelf. Our own rows don't drop back in.
        var accept = _acceptDrag;
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        SetDropTarget(accept);
    }

    void OnDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also bubbles up when the pointer only crosses from one child to another; ignore it while still over the card.
        var p = e.GetPosition(Card);
        if (p.X >= 0 && p.Y >= 0 && p.X < Card.ActualWidth && p.Y < Card.ActualHeight) return;
        _acceptDrag = _dragDecided = false;
        SetDropTarget(false);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        SetDropTarget(false);
        var accepted = _acceptDrag;
        _acceptDrag = _dragDecided = false;
        if (_draggingOut || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        if (!accepted)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        Add(paths);
    }

    void SetDropTarget(bool on)
    {
        DropRing.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        HintText.Text = on ? "여기에 놓기" : "끌어서 꺼내기";
    }
}
