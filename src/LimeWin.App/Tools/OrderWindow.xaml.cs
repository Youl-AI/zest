using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LimeWin.Core.Pdf;
using LimeWin.Core.Tools;
using Wpf.Ui.Controls;

namespace LimeWin.App.Tools;

public enum OrderMode { ImagesToPdf, MergePdfs }

/// <summary>One row of the order list.</summary>
public sealed class OrderItem : INotifyPropertyChanged
{
    int _position;
    string _detail;
    ImageSource? _thumbnail;

    public OrderItem(string path, bool isPdf)
    {
        Path = path;
        IsPdf = isPdf;
        _detail = SizeText(path);
    }

    public string Path { get; }
    public bool IsPdf { get; }
    public string Name => System.IO.Path.GetFileName(Path);

    public int Position { get => _position; set => Set(ref _position, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            Set(ref _thumbnail, value);
            OnChanged(nameof(ThumbnailVisibility));
            OnChanged(nameof(GlyphVisibility));
        }
    }

    public Visibility ThumbnailVisibility => _thumbnail is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GlyphVisibility => _thumbnail is null && IsPdf ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged(name!);
    }

    void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    internal static string SizeText(string path)
    {
        try
        {
            return ImageCompressor.FormatSize(new FileInfo(path).Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}

/// <summary>
/// Puts the files for PDF 만들기 / 합치기 in order: drag a row (it moves as the pointer crosses other rows, no animation)
/// or use ↑/↓; the primary button hands the paths, in order, to the caller.
/// </summary>
public partial class OrderWindow : FluentWindow
{
    const double ThumbnailDip = 40;

    readonly ObservableCollection<OrderItem> _items;
    readonly Action<IReadOnlyList<string>> _go;
    Point? _pressAt;
    OrderItem? _dragging;
    volatile bool _closed;
    readonly DispatcherTimer _autoScroll = new() { Interval = TimeSpan.FromMilliseconds(80) };
    const double AutoScrollEdge = 24;

    public OrderWindow(OrderMode mode, IReadOnlyList<OrderItem> items, Action<IReadOnlyList<string>> go)
    {
        _items = new ObservableCollection<OrderItem>(items);
        _go = go;
        InitializeComponent();

        var (title, verb) = mode == OrderMode.ImagesToPdf ? ("PDF 만들기", "만들기") : ("PDF 합치기", "합치기");
        Title = title;
        Bar.Title = title;
        GoButton.Content = verb;

        List.ItemsSource = _items;
        Renumber();
        List.SelectedIndex = 0;
        List.SelectionChanged += (_, _) => UpdateArrows();
        UpButton.Click += (_, _) => Shift(-1);
        DownButton.Click += (_, _) => Shift(+1);
        GoButton.Click += (_, _) =>
        {
            _go(_items.Select(i => i.Path).ToList());
            Close();
        };

        List.PreviewMouseLeftButtonDown += OnPress;
        List.PreviewMouseMove += OnDragMove;
        List.PreviewMouseLeftButtonUp += (_, _) => EndDrag();
        List.LostMouseCapture += (_, _) => EndDrag();
        _autoScroll.Tick += (_, _) => AutoScrollTick();
        Closed += (_, _) =>
        {
            _closed = true;
            _autoScroll.Stop();
        };
        UpdateArrows();
    }

    /// <summary>Rows for the files, in the order they were dropped.</summary>
    public static IReadOnlyList<OrderItem> ItemsFor(IReadOnlyList<string> files, OrderMode mode) =>
        files.Select(f => new OrderItem(f, mode == OrderMode.MergePdfs)).ToList();

    /// <summary>
    /// Fills thumbnails (images) or page counts (PDFs) one file after another on a worker thread, so the window is usable
    /// at once. A file that cannot be read keeps its plain row; the job reports the problem.
    /// </summary>
    public void LoadDetailsInBackground(double monitorScale)
    {
        var items = _items.ToList();
        var edge = (int)Math.Ceiling(ThumbnailDip * monitorScale);
        var dispatcher = Dispatcher;
        _ = Task.Run(async () =>
        {
            foreach (var item in items)
            {
                try
                {
                    if (item.IsPdf)
                    {
                        var pages = await PdfRenderer.PageCountAsync(item.Path);
                        var detail = $"{pages}쪽 · {OrderItem.SizeText(item.Path)}";
                        dispatcher.Invoke(() => item.Detail = detail);
                    }
                    else
                    {
                        var preview = ImagePreview.Load(item.Path, edge);
                        var bitmap = ToolDispatcher.ToBitmap(preview);
                        var detail = $"{preview.SourceWidth} × {preview.SourceHeight} · {OrderItem.SizeText(item.Path)}";
                        dispatcher.Invoke(() =>
                        {
                            item.Thumbnail = bitmap;
                            item.Detail = detail;
                        });
                    }
                }
                catch (Exception)
                {
                    // Best effort: leave the row as it is (the job itself reports an unreadable file).
                }
                if (_closed) return;
            }
        });
    }

    void Renumber()
    {
        for (var i = 0; i < _items.Count; i++) _items[i].Position = i + 1;
    }

    void UpdateArrows()
    {
        var i = List.SelectedIndex;
        UpButton.IsEnabled = i > 0;
        DownButton.IsEnabled = i >= 0 && i < _items.Count - 1;
    }

    void Shift(int by)
    {
        var from = List.SelectedIndex;
        var to = from + by;
        if (from < 0 || to < 0 || to >= _items.Count) return;
        MoveItem(from, to);
    }

    void MoveItem(int from, int to)
    {
        _items.Move(from, to);
        Renumber();
        List.SelectedIndex = to;
        List.ScrollIntoView(_items[to]);
        UpdateArrows();
    }

    /// <summary>A drag starts only on a row itself, never on the scroll bar (which would otherwise pick the row at that height).</summary>
    void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressAt = null;
        _dragging = null;
        if (e.OriginalSource is not DependencyObject source || IsInside<ScrollBar>(source)) return;
        if (ItemsControl.ContainerFromElement(List, source) is not ListBoxItem row) return;
        _pressAt = e.GetPosition(List);
        _dragging = row.DataContext as OrderItem;
    }

    void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || _pressAt is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(List);
        if (!List.IsMouseCaptured)
        {
            if (Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            List.SelectedItem = _dragging;
            List.CaptureMouse();
            _autoScroll.Start();
        }
        FollowPointer(p);
        e.Handled = true;
    }

    /// <summary>The row follows the pointer: once it is over another row, the dragged row takes that row's place.</summary>
    void FollowPointer(Point p)
    {
        if (_dragging is not null && ItemAt(p) is { } over && over != _dragging)
            MoveItem(_items.IndexOf(_dragging), _items.IndexOf(over));
    }

    /// <summary>While a row is dragged near the list's top or bottom edge, scroll one row per tick and keep following the pointer.</summary>
    void AutoScrollTick()
    {
        if (_dragging is null || !List.IsMouseCaptured || Descendant<ScrollViewer>(List) is not { } viewer) return;
        var p = Mouse.GetPosition(List);
        if (p.Y < AutoScrollEdge) viewer.LineUp();
        else if (p.Y > List.ActualHeight - AutoScrollEdge) viewer.LineDown();
        else return;
        List.UpdateLayout();
        FollowPointer(new Point(p.X, Math.Clamp(p.Y, 1, List.ActualHeight - 1)));
    }

    void EndDrag()
    {
        _autoScroll.Stop();
        _pressAt = null;
        _dragging = null;
        if (List.IsMouseCaptured) List.ReleaseMouseCapture();
    }

    static bool IsInside<T>(DependencyObject d) where T : DependencyObject
    {
        for (var node = d; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is T) return true;
        return false;
    }

    static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (Descendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    OrderItem? ItemAt(Point p)
    {
        foreach (var item in _items)
        {
            if (List.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem row || !row.IsVisible) continue;
            var top = row.TranslatePoint(new Point(0, 0), List).Y;
            if (p.Y >= top && p.Y < top + row.ActualHeight) return item;
        }
        return null;
    }
}
