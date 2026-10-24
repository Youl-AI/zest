using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zest.App.Jobs;
using Zest.App.Shelf;
using Zest.App.Tools;
using Zest.App.Tray;
using Zest.App.Wheel;
using Zest.Core.Catalog;
using Zest.Core.Design;
using Zest.Core.Jobs;
using Zest.Core.Tools;
using Zest.Core.Wheel;
using Path = System.Windows.Shapes.Path;

namespace Zest.App.Render;

/// <summary>A file in the demo's folder: its name, the sample landscape its thumbnail shows (null: a document icon) and its size.</summary>
internal sealed record DemoFile(string Name, int? Photo, long Bytes);

/// <summary>
/// What changes with the README's language: the captions, the end card's line, and the Explorer window, taskbar and file
/// names around the app. The app's own text (wheel, cards, compress window, shelf) stays Korean, as it is in the app.
/// </summary>
internal sealed record DemoCopy(
    string Code, string Folder, string Search, string New, string Sort, string View, string[] Nav,
    Func<int, string> Items, Func<string, string> Selected, string Time, string Date,
    string ConvertLine, string ToolsLine, string ActionsLine, string Tagline,
    DemoFile[] Files, DemoFile Sea, DemoFile Report, DemoFile Jpg, DemoFile Compressed)
{
    public static DemoCopy Korean()
    {
        var sea = new DemoFile("제주 바다.png", 0, 5_600_000);
        var report = new DemoFile("보고서.pdf", null, 1_240_000);
        return new DemoCopy("ko", "사진", "사진 검색", "새로 만들기", "정렬", "보기",
            ["홈", "갤러리", "바탕 화면", "다운로드", "문서", "사진", "음악", "동영상"],
            n => $"{n}개 항목", size => $"1개 항목 선택함 {size}", "오후 3:12", "2026-10-24",
            "끌다가 탭하면 변환 휠", "도구 휠 · 압축, 자르기, 메타데이터", "액션 휠 · 선반에 잠시 올려 두기", RenderHarness.TaglineKo,
            [new("노을.png", 1, 3_100_000), report, sea, new("하늘.jpg", 2, 2_400_000), new("회의록.docx", null, 320_000)],
            sea, report, new("제주 바다.jpg", 0, 2_100_000), new("제주 바다 (compressed).jpg", 0, 880_000));
    }

    public static DemoCopy English()
    {
        var sea = new DemoFile("jeju-sea.png", 0, 5_600_000);
        var report = new DemoFile("report.pdf", null, 1_240_000);
        return new DemoCopy("en", "Pictures", "Search Pictures", "New", "Sort", "View",
            ["Home", "Gallery", "Desktop", "Downloads", "Documents", "Pictures", "Music", "Videos"],
            n => $"{n} items", size => $"1 item selected  {size}", "3:12 PM", "10/24/2026",
            "Tap mid-drag for the convert wheel", "Tools wheel · compress, crop, metadata", "Actions wheel · park files on the shelf", RenderHarness.TaglineEn,
            [new("dusk.png", 1, 3_100_000), sea, new("notes.docx", null, 320_000), report, new("sky.jpg", 2, 2_400_000)],
            sea, report, new("jeju-sea.jpg", 0, 2_100_000), new("jeju-sea (compressed).jpg", 0, 880_000));
    }
}

/// <summary>
/// The README demo as a film that reads like a screen recording: over a desktop with an Explorer window, the cursor picks up a
/// photo, a key tap opens the real wheel at the cursor, the highlight follows the cursor through <see cref="WheelGeometry.PetalAt"/>
/// as on screen, and the drop runs into a real progress card (convert), the compress window and a card (tools) or the shelf
/// (actions); an end card closes it, and its fade-out shows the first frame again, so the loop has no seam. The wheel, card,
/// window and shelf are the app's own views; the desktop, Explorer and cursor are drawn here. Motion follows the app's
/// (200 ms entrances and 150 ms exits on the strong ease-out, the wheel appearing at once); the cursor moves on a
/// minimum-jerk curve along a slight arc and rests a moment before each press and release so the eye can follow.
/// </summary>
internal sealed class DemoMovie
{
    /// <summary>The desktop, in DIP: a 1120 x 630 piece of a screen, drawn at 1920 x 1080 for the video.</summary>
    const double W = 1120, H = 630, Taskbar = 44, WorkHeight = H - Taskbar;
    const int Fps = 30, VideoWidth = 1920, VideoHeight = 1080, WebpWidth = 960, WebpFps = 24;
    const double OutScale = VideoWidth / W;

    // Explorer: the window, its title and command rows, the navigation pane and the icon grid.
    const double WinX = 28, WinY = 74, WinW = 684, WinH = 492, BodyTop = 129, StatusHeight = 24, NavWidth = 156;
    const double TileW = 104, TileH = 118, TileStepX = 112, TileStepY = 126, Columns = 4;
    static readonly Point GridOrigin = new(WinX + NavWidth + 20, WinY + BodyTop + 14);

    /// <summary>Where the drag image sits under the cursor tip.</summary>
    static readonly Vector GhostGrip = new(44, 34);

    static readonly Func<double, double> EaseOut = Bezier(0.23, 1, 0.32, 1), Ease = Bezier(0.25, 0.1, 0.25, 1);
    static readonly WheelGeometry Geometry = new();

    sealed record Move(double Start, double End, Point From, Point To, double Bend);
    sealed record Press(double At, DemoFile File);
    sealed record Drag(double Start, double End, DemoFile File, FrameworkElement Ghost);

    sealed class Tile(FrameworkElement root, Border box, ScaleTransform scale, Rect bounds, DemoFile file)
    {
        public FrameworkElement Root { get; } = root;
        public Border Box { get; } = box;
        public ScaleTransform Scale { get; } = scale;
        public Rect Bounds { get; } = bounds;
        public DemoFile File { get; } = file;
        public double? AppearAt { get; set; }
        public Point Center => new(Bounds.X + TileW / 2, Bounds.Y + 40);
    }

    sealed class WheelShow(WheelMode mode, IReadOnlyList<WheelItem> items, string summary, Point center, double open)
    {
        public WheelMode Mode { get; } = mode;
        public IReadOnlyList<WheelItem> Items { get; } = items;
        public string Summary { get; } = summary;
        public Point Center { get; } = center;
        public double Open { get; } = open;
        public double Close { get; set; } = double.MaxValue;
        public Dictionary<int, BitmapSource> Pictures { get; } = [];
        public bool Seen;
        public int? Shown, Previous;
        public double ChangedAt;
    }

    sealed class Caption(FrameworkElement pill, TranslateTransform shift, Border chip, ScaleTransform chipScale, WheelMode mode, double @in)
    {
        public FrameworkElement Pill { get; } = pill;
        public TranslateTransform Shift { get; } = shift;
        public Border Chip { get; } = chip;
        public ScaleTransform ChipScale { get; } = chipScale;
        public WheelMode Mode { get; } = mode;
        public double In { get; } = @in;
        public double Out { get; set; } = double.MaxValue;
        public double Tap { get; set; } = double.MaxValue;
    }

    sealed class Job(ProgressCard card, FrameworkElement holder, TranslateTransform rise, DemoFile result, double @in, double done)
    {
        public ProgressCard Card { get; } = card;
        public FrameworkElement Holder { get; } = holder;
        public TranslateTransform Rise { get; } = rise;
        public DemoFile Result { get; } = result;
        public double In { get; } = @in;
        public double Done { get; } = done;
        public double Out { get; set; } = double.MaxValue;
        public bool Finished { get; set; }
    }

    readonly DemoCopy _copy;
    readonly Canvas _stage = new() { Width = W, Height = H };
    readonly Canvas _tiles = new() { Width = W, Height = H };
    readonly Canvas _popups = new() { Width = W, Height = H };
    readonly Canvas _ghosts = new() { Width = W, Height = H };
    readonly Canvas _overlay = new() { Width = W, Height = H, IsHitTestVisible = false };
    readonly TextBlock _status;
    readonly Image _wheelUnder = new() { Width = WheelGeometry.CanvasSide, Height = WheelGeometry.CanvasSide };
    readonly Image _wheelOver = new() { Width = WheelGeometry.CanvasSide, Height = WheelGeometry.CanvasSide };
    readonly Path _cursor;
    readonly TranslateTransform _cursorAt = new();
    readonly Grid _end;
    readonly TranslateTransform _endRise = new();
    readonly RenderTargetBitmap _target = new(VideoWidth, VideoHeight, 96 * OutScale, 96 * OutScale, PixelFormats.Pbgra32);

    readonly List<Tile> _tileList = [];
    readonly List<Move> _moves = [];
    readonly List<Press> _presses = [];
    readonly List<Drag> _drags = [];
    readonly List<WheelShow> _wheels = [];
    readonly List<Caption> _captions = [];
    readonly List<Job> _jobs = [];
    readonly Dictionary<int, BitmapSource> _photos = [];

    // The compress window: its picture, the same with 압축 pressed, and when it opens, is pressed and closes.
    Image? _dialog, _dialogPressed;
    ScaleTransform? _dialogScale;
    double _dialogIn = double.MaxValue, _dialogPress = double.MaxValue, _dialogOut = double.MaxValue;
    Point _dialogButton;

    Image? _shelf;
    double _shelfIn = double.MaxValue;

    double _clock, _endIn, _resetAt, _endOut;
    Point _pointer = new(560, 400);
    int _bendSign = 1;

    public double Duration { get; private set; }

    /// <summary>The time of the poster frame: the convert wheel open with JPG highlighted.</summary>
    public double PosterAt { get; private set; }

    DemoMovie(DemoCopy copy)
    {
        _copy = copy;
        RenderHarness.AsOnHighDpiScreen(_stage);
        _stage.Background = Brushes.White;
        _stage.Children.Add(new Image { Source = RenderHarness.Snapshot(Desktop(), W, H, OutScale), Width = W, Height = H });

        _status = new TextBlock { FontFamily = WheelStyle.TextFont, FontSize = 11, Foreground = Hex("#5C5C5C") };
        Canvas.SetLeft(_status, WinX + 14);
        Canvas.SetTop(_status, WinY + WinH - StatusHeight + 4);
        _stage.Children.Add(_status);

        _tiles.Clip = new RectangleGeometry(new Rect(WinX + NavWidth + 1, WinY + BodyTop, WinW - NavWidth - 2, WinH - BodyTop - StatusHeight));
        _stage.Children.Add(_tiles);
        for (var i = 0; i < copy.Files.Length; i++) AddTile(copy.Files[i], i);
        AddTile(copy.Jpg, copy.Files.Length);
        AddTile(copy.Compressed, copy.Files.Length + 1);

        _stage.Children.Add(_popups);
        _stage.Children.Add(_ghosts); // the drag image stays under the wheel, which opens over it
        _stage.Children.Add(_wheelUnder);
        _stage.Children.Add(_wheelOver);
        _stage.Children.Add(_overlay);

        _cursor = new Path
        {
            // The standard arrow, tip at (0, 0), drawn a little larger than 1:1 so it reads at README size.
            Data = System.Windows.Media.Geometry.Parse("M0,0 L0,17 L4.2,13.2 L6.9,19.3 L9.7,18.1 L7,12.1 L12.6,12.1 Z"),
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1.5, Direction = 270, Opacity = 0.35 },
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1.15, 1.15), _cursorAt } },
        };
        _stage.Children.Add(_cursor);

        _end = new Grid { Width = W, Height = H, Background = Hex(ZestColors.Light.Window), Opacity = 0 };
        var lockup = new Grid { Width = W, Height = H, RenderTransform = _endRise };
        RenderHarness.AddLockup(lockup, copy.Tagline, dark: false, H, mark: 112, title: 54, line: 20, glow: 280);
        lockup.Children.Add(new TextBlock
        {
            Text = "github.com/Youl-AI/zest",
            FontFamily = WheelStyle.ChipFont,
            FontSize = 14,
            Foreground = Hex(ZestColors.Light.Text3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 72),
        });
        _end.Children.Add(lockup);
        _stage.Children.Add(_end);

        Script();
        _stage.Measure(new Size(W, H));
        _stage.Arrange(new Rect(0, 0, W, H));
    }

    // ── The script ──────────────────────────────────────────────────────────

    /// <summary>
    /// The three wheels, one scene each. Every step advances the clock; a step that takes time on screen (a cursor move, a
    /// job) adds its own length, and waits are the pauses between, long enough to read the hub or the caption.
    /// </summary>
    void Script()
    {
        var c = _copy;

        // 1. Convert: the photo to JPG; the new file joins the folder.
        Wait(0.25);
        var convertLine = ShowCaption(WheelMode.Convert, c.ConvertLine);
        Wait(0.4);
        MoveTo(TileOf(c.Sea).Center);
        Wait(0.3);
        PressOn(c.Sea);
        MoveTo(new Point(858, 300));
        Wait(0.4);
        var convert = OpenWheel(convertLine, c.Sea);
        Wait(1.0);
        MoveTo(PetalPoint(convert, op => op is ConvertOp { Target: var t } && t.Equals("jpg", StringComparison.OrdinalIgnoreCase)), bend: 0.02);
        Wait(0.45);
        PosterAt = _clock - 0.1;
        Release(convert);
        Wait(0.1);
        var toJpg = StartJob("JPG 변환", c.Sea.Name, JobKind.Convert, c.Jpg, run: 1.2);
        Wait(toJpg.Done - _clock + 1.4);

        // 2. Tools: the new JPG into 압축, the window's 압축 button, then a card and the smaller copy.
        toJpg.Out = _clock;
        convertLine.Out = _clock;
        Wait(0.25);
        var toolsLine = ShowCaption(WheelMode.Tools, c.ToolsLine);
        Wait(0.3);
        MoveTo(TileOf(c.Jpg).Center);
        Wait(0.3);
        PressOn(c.Jpg);
        MoveTo(new Point(846, 292));
        Wait(0.35);
        var tools = OpenWheel(toolsLine, c.Jpg);
        Wait(0.85);
        MoveTo(PetalPoint(tools, op => op is ToolOp { Tool: ZestTool.Compress }), bend: 0.02);
        Wait(0.4);
        Release(tools);
        Wait(0.1);
        OpenDialog(c.Jpg);
        Wait(0.8);
        MoveTo(_dialogButton);
        Wait(0.3);
        _dialogPress = _clock;
        _dialogOut = _clock + 0.14;
        Wait(0.24);
        var compress = StartJob("압축", c.Jpg.Name, JobKind.Tool, c.Compressed, run: 1.3);
        Wait(compress.Done - _clock + 1.4);

        // 3. Actions: the report onto the shelf, which opens where it was dropped.
        compress.Out = _clock;
        toolsLine.Out = _clock;
        Wait(0.25);
        var actionsLine = ShowCaption(WheelMode.Actions, c.ActionsLine);
        Wait(0.3);
        MoveTo(TileOf(c.Report).Center);
        Wait(0.3);
        PressOn(c.Report);
        MoveTo(new Point(850, 296));
        Wait(0.35);
        var actions = OpenWheel(actionsLine, c.Report);
        Wait(0.85);
        MoveTo(PetalPoint(actions, op => op is ActionOp { Action: DropAction.Shelf }), bend: 0.02);
        Wait(0.4);
        Release(actions);
        OpenShelf(c.Report);
        Wait(0.45);
        MoveTo(new Point(704, 472));
        Wait(1.6);

        // The end card; under it the desktop goes back to its first frame, which its fade-out reveals.
        actionsLine.Out = _clock;
        _endIn = _clock + 0.1;
        _resetAt = _endIn + 0.35;
        _endOut = _resetAt + 2.3;
        Duration = _endOut + 0.4;
    }

    void Wait(double seconds) => _clock += seconds;

    /// <summary>
    /// Moves the cursor to <paramref name="to"/> on a slight arc, alternating sides. Longer moves take longer, but less than in
    /// proportion (0.35 s plus 0.75 ms a DIP, at most 0.85 s), as a hand does.
    /// </summary>
    void MoveTo(Point to, double bend = 0.07)
    {
        var length = (to - _pointer).Length;
        var duration = Math.Min(0.35 + 0.00075 * length, 0.85);
        _moves.Add(new Move(_clock, _clock + duration, _pointer, to, bend * _bendSign));
        _bendSign = -_bendSign;
        _pointer = to;
        _clock += duration;
    }

    void PressOn(DemoFile file)
    {
        _presses.Add(new Press(_clock, file));
        var ghost = Art(file);
        ghost.Opacity = 0;
        _ghosts.Children.Add(ghost);
        _drags.Add(new Drag(_clock, double.MaxValue, file, ghost));
    }

    /// <summary>The key tap (the caption's chip presses) and, 50 ms later, the wheel for the dragged file at the cursor.</summary>
    WheelShow OpenWheel(Caption caption, DemoFile file)
    {
        caption.Tap = _clock;
        Wait(0.05);
        var items = WheelCatalog.Items([file.Name], caption.Mode, Capabilities.All);
        var wheel = new WheelShow(caption.Mode, items, HubSummary.For([new HubEntry(IsFolder: false, file.Bytes)]), _pointer, _clock);
        _wheels.Add(wheel);
        return wheel;
    }

    /// <summary>The middle of the first petal whose operation matches, where its label sits.</summary>
    static Point PetalPoint(WheelShow wheel, Func<Operation, bool> match)
    {
        var index = wheel.Items.ToList().FindIndex(item => match(item.Operation));
        if (index < 0) throw new InvalidOperationException($"the {wheel.Mode} wheel has no such petal");
        var at = PetalPath.At(PetalPath.Bearing(index, wheel.Items.Count), WheelStyle.LabelRadius) - PetalPath.Center;
        return wheel.Center + at;
    }

    void Release(WheelShow wheel)
    {
        wheel.Close = _clock;
        var drag = _drags[^1];
        _drags[^1] = drag with { End = _clock };
    }

    Caption ShowCaption(WheelMode mode, string line)
    {
        var chip = RenderHarness.KeyChip(mode, dark: false, active: true, size: 13);
        var chipScale = new ScaleTransform(1, 1);
        chip.RenderTransformOrigin = new Point(0.5, 0.5);
        chip.RenderTransform = chipScale;
        chip.VerticalAlignment = VerticalAlignment.Center;
        var surface = ZestColors.Light;
        var shift = new TranslateTransform();
        var pill = new Border
        {
            Background = Hex(surface.Window),
            BorderBrush = Hex(surface.LineStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(6, 6, 16, 6),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.1 },
            RenderTransform = shift,
            Opacity = 0,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    chip,
                    new TextBlock
                    {
                        Text = line,
                        FontFamily = WheelStyle.ChipFont,
                        FontSize = 15,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Hex(surface.Text),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 1),
                    },
                },
            },
        };
        pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(pill, Math.Round((W - pill.DesiredSize.Width) / 2));
        Canvas.SetTop(pill, 18);
        _overlay.Children.Add(pill);
        var caption = new Caption(pill, shift, chip, chipScale, mode, _clock);
        _captions.Add(caption);
        return caption;
    }

    /// <summary>A progress card at the bottom right of the work area, as the host places it, and the tile its result becomes.</summary>
    Job StartJob(string title, string detail, JobKind kind, DemoFile result, double run)
    {
        var card = new ProgressCard(title, detail, kind, () => { });
        var rise = new TranslateTransform();
        var holder = new Grid { Children = { card }, RenderTransform = rise, Opacity = 0 };
        holder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(holder, W - ProgressCard.OuterWidth);
        Canvas.SetTop(holder, WorkHeight - Math.Ceiling(holder.DesiredSize.Height));
        _popups.Children.Add(holder);
        var job = new Job(card, holder, rise, result, _clock, _clock + run);
        TileOf(result).AppearAt = job.Done + 0.1;
        _jobs.Add(job);
        return job;
    }

    /// <summary>The compress window centred in the work area, as tool windows open, with a copy whose 압축 is pressed.</summary>
    void OpenDialog(DemoFile file)
    {
        var window = new CompressOptionsWindow([new CompressSource($@"C:\Users\me\Pictures\{file.Name}", file.Bytes)], _ => { });
        window.Preview(SizeGoal.Percentage);
        RenderHarness.AsOnHighDpiScreen(window);
        window.ApplyTemplate();
        var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        root.Measure(new Size(window.Width, double.PositiveInfinity));
        var size = new Size(window.Width, Math.Ceiling(root.DesiredSize.Height));
        // The frame's shadow reaches past the window's bounds below and at the sides; draw it whole.
        var bleed = new Thickness(12, 0, 12, 24);
        root.Arrange(new Rect(new Point(bleed.Left, bleed.Top), size));
        root.UpdateLayout();
        var drawn = new Size(size.Width + bleed.Left + bleed.Right, size.Height + bleed.Top + bleed.Bottom);
        BitmapSource Picture()
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(drawn.Width * OutScale), (int)Math.Ceiling(drawn.Height * OutScale),
                96 * OutScale, 96 * OutScale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            bitmap.Freeze();
            return bitmap;
        }
        var normal = Picture();
        var button = window.CompressButton;
        var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = new ScaleTransform(0.97, 0.97);
        root.UpdateLayout();
        var pressed = Picture();
        window.Close();

        var left = Math.Round((W - size.Width) / 2) - bleed.Left;
        var top = Math.Round((WorkHeight - size.Height) / 2) - bleed.Top;
        _dialogButton = new Point(left + bleed.Left + bounds.X + bounds.Width / 2, top + bleed.Top + bounds.Y + bounds.Height / 2 + 2);
        _dialogScale = new ScaleTransform(1, 1);
        // Scales about the frame's centre, as the window's own entrance does.
        var origin = new Point((bleed.Left + size.Width / 2) / drawn.Width, (bleed.Top + size.Height / 2) / drawn.Height);
        Image Place(BitmapSource picture)
        {
            var image = new Image { Source = picture, Width = drawn.Width, Height = drawn.Height, Opacity = 0, RenderTransformOrigin = origin, RenderTransform = _dialogScale };
            Canvas.SetLeft(image, left);
            Canvas.SetTop(image, top);
            _popups.Children.Add(image);
            return image;
        }
        _dialog = Place(normal);
        _dialogPressed = Place(pressed);
        _dialogIn = _clock;
    }

    /// <summary>The shelf holding the dropped file, centred on the drop point and kept in the work area (it opens without motion, as in the app).</summary>
    void OpenShelf(DemoFile file)
    {
        var icon = RenderHarness.Snapshot(Art(file), 44, 56, 4);
        var window = new ShelfWindow([new ShelfItem($@"C:\Users\me\Pictures\{file.Name}", icon, icon, missing: false)], listView: false);
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.Resources.MergedDictionaries.Add(window.Resources); // lifted content keeps the window's styles and accent
        RenderHarness.AsOnHighDpiScreen(content);
        content.Width = window.Width;
        window.Close();
        const double Room = 24;
        var holder = new Border { Padding = new Thickness(Room), Child = content };
        holder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = holder.DesiredSize;
        _shelf = new Image { Source = RenderHarness.Snapshot(holder, size.Width, size.Height, OutScale), Width = size.Width, Height = size.Height, Opacity = 0 };
        var inner = new Size(size.Width - 2 * Room, size.Height - 2 * Room);
        var left = Math.Clamp(_pointer.X - inner.Width / 2, 8, W - inner.Width - 8);
        var top = Math.Clamp(_pointer.Y - inner.Height / 2, 8, WorkHeight - inner.Height - 8);
        Canvas.SetLeft(_shelf, Math.Round(left - Room));
        Canvas.SetTop(_shelf, Math.Round(top - Room));
        _popups.Children.Add(_shelf);
        _shelfIn = _clock;
    }

    // ── Frames ──────────────────────────────────────────────────────────────

    /// <summary>Sets every element for time <paramref name="t"/>.</summary>
    void Apply(double t)
    {
        var endIn = EaseOut(Progress(t, _endIn, 0.35));
        var endOut = EaseOut(Progress(t, _endOut, 0.4));
        _end.Opacity = endIn * (1 - endOut);
        _endRise.Y = 8 * (1 - EaseOut(Progress(t, _endIn + 0.06, 0.35)));
        // Under the opaque end card the desktop is back at its first frame.
        var s = t >= _resetAt ? 0 : t;

        var pointer = PointerAt(s);
        _cursorAt.X = pointer.X;
        _cursorAt.Y = pointer.Y;
        var drag = _drags.LastOrDefault(d => s >= d.Start && s < d.End);
        foreach (var d in _drags)
        {
            var on = d == drag && s >= d.Start + 0.12;
            // The wheel opens over the drag image; it fades out in 100 ms so it does not show between the petals.
            var wheel = _wheels.FirstOrDefault(w => w.Open >= d.Start && w.Open < d.End);
            var covered = wheel is null ? 0 : EaseOut(Progress(s, wheel.Open, 0.1));
            d.Ghost.Opacity = on ? 0.72 * EaseOut(Progress(s, d.Start + 0.12, 0.1)) * (1 - covered) : 0;
            if (!on) continue;
            Canvas.SetLeft(d.Ghost, pointer.X - GhostGrip.X);
            Canvas.SetTop(d.Ghost, pointer.Y - GhostGrip.Y);
        }

        ApplyTiles(s, pointer, drag);
        ApplyCaptions(s);
        ApplyWheel(s, pointer);
        ApplyJobs(s);

        if (_dialog is not null && _dialogPressed is not null && _dialogScale is not null)
        {
            var enter = EaseOut(Progress(s, _dialogIn, 0.2));
            var exit = EaseOut(Progress(s, _dialogOut, 0.15));
            var opacity = s < _dialogIn ? 0 : enter * (1 - exit);
            var pressed = s >= _dialogPress && s < _dialogOut;
            _dialog.Opacity = pressed ? 0 : opacity;
            _dialogPressed.Opacity = pressed ? opacity : 0;
            _dialogScale.ScaleX = _dialogScale.ScaleY = 0.96 + 0.04 * enter - 0.04 * exit;
        }
        if (_shelf is not null) _shelf.Opacity = s >= _shelfIn ? 1 : 0;
    }

    Point PointerAt(double t)
    {
        var at = _moves.Count > 0 ? _moves[0].From : _pointer;
        foreach (var move in _moves)
        {
            if (t < move.Start) break;
            if (t >= move.End)
            {
                at = move.To;
                continue;
            }
            // Minimum jerk: the speed profile of a hand reaching for a point.
            var p = (t - move.Start) / (move.End - move.Start);
            var reach = p * p * p * (10 + p * (-15 + 6 * p));
            var line = move.To - move.From;
            var normal = new Vector(-line.Y, line.X);
            return move.From + line * reach + normal * (move.Bend * 4 * reach * (1 - reach));
        }
        return at;
    }

    void ApplyTiles(double t, Point pointer, Drag? drag)
    {
        var selected = _presses.LastOrDefault(p => p.At <= t)?.File;
        var count = 0;
        foreach (var tile in _tileList)
        {
            var appear = tile.AppearAt is { } at ? EaseOut(Progress(t, at, 0.2)) : 1;
            var shown = tile.AppearAt is not { } from || t >= from;
            tile.Root.Opacity = shown ? appear : 0;
            tile.Scale.ScaleX = tile.Scale.ScaleY = 0.95 + 0.05 * appear;
            if (shown) count++;

            var hover = drag is null && shown && tile.Bounds.Contains(pointer);
            var (fill, edge) = tile.File == selected ? ("#D5E7F7", "#B6D4EE") : hover ? ("#EEF4FA", "#EEF4FA") : ("#00FFFFFF", "#00FFFFFF");
            tile.Box.Background = Hex(fill);
            tile.Box.BorderBrush = Hex(edge);
        }
        var size = selected is null ? null : HubSummary.For([new HubEntry(IsFolder: false, selected.Bytes)]);
        _status.Text = size is null ? _copy.Items(count) : $"{_copy.Items(count)}    {_copy.Selected(size)}";
    }

    /// <summary>Captions rise 6 DIP and fade in over 250 ms and leave faster (150 ms, 4 DIP up); a key tap presses the chip.</summary>
    void ApplyCaptions(double t)
    {
        foreach (var caption in _captions)
        {
            var enter = EaseOut(Progress(t, caption.In, 0.25));
            var exit = EaseOut(Progress(t, caption.Out, 0.15));
            caption.Pill.Opacity = t < caption.In ? 0 : enter * (1 - exit);
            caption.Shift.Y = 6 * (1 - enter) - 4 * exit;

            // The press goes down in 100 ms and comes back in 160 ms: the chip shrinks a little and fills with its citrus.
            var since = t - caption.Tap;
            var press = since < 0 ? 0 : since < 0.1 ? EaseOut(since / 0.1) : 1 - EaseOut(Math.Min((since - 0.1) / 0.16, 1));
            caption.ChipScale.ScaleX = caption.ChipScale.ScaleY = 1 - 0.08 * press;
            var citrus = WheelStyle.Accent(caption.Mode);
            caption.Chip.Background = new SolidColorBrush(Mix(citrus.Wash(false), citrus.Fill, press));
            ((TextBlock)caption.Chip.Child).Foreground = new SolidColorBrush(Mix(citrus.Ink(false), citrus.OnFill, press));
        }
    }

    /// <summary>
    /// The open wheel, highlighting what <see cref="WheelGeometry.PetalAt"/> finds under the cursor. A change crossfades
    /// over 100 ms from the last state's picture, standing in for the petal's colour change.
    /// </summary>
    void ApplyWheel(double t, Point pointer)
    {
        var wheel = _wheels.FirstOrDefault(w => t >= w.Open && t < w.Close);
        if (wheel is null)
        {
            _wheelUnder.Opacity = _wheelOver.Opacity = 0;
            return;
        }
        var under = pointer - wheel.Center;
        var highlight = Geometry.PetalAt(under.X, under.Y, wheel.Items.Count);
        if (!wheel.Seen)
        {
            wheel.Seen = true;
            wheel.Shown = wheel.Previous = highlight;
        }
        else if (highlight != wheel.Shown)
        {
            wheel.Previous = wheel.Shown;
            wheel.Shown = highlight;
            wheel.ChangedAt = t;
        }
        var blend = Ease(Progress(t, wheel.ChangedAt, 0.1));
        foreach (var image in new[] { _wheelUnder, _wheelOver })
        {
            Canvas.SetLeft(image, wheel.Center.X - PetalPath.Center.X);
            Canvas.SetTop(image, wheel.Center.Y - PetalPath.Center.Y);
        }
        _wheelUnder.Source = WheelPicture(wheel, wheel.Previous);
        _wheelUnder.Opacity = blend < 1 ? 1 : 0;
        _wheelOver.Source = WheelPicture(wheel, wheel.Shown);
        _wheelOver.Opacity = blend;
    }

    BitmapSource WheelPicture(WheelShow wheel, int? highlight)
    {
        var key = highlight ?? -1;
        if (!wheel.Pictures.TryGetValue(key, out var picture))
        {
            var view = RenderHarness.Wheel(null, WheelStyle.LightPalette, wheel.Mode, wheel.Items, highlight, wheel.Summary);
            wheel.Pictures[key] = picture = RenderHarness.Snapshot(view, WheelGeometry.CanvasSide, WheelGeometry.CanvasSide, OutScale);
        }
        return picture;
    }

    /// <summary>Cards rise 8 DIP and fade in over 200 ms, fill linearly while running, and sink and fade over 150 ms.</summary>
    void ApplyJobs(double t)
    {
        foreach (var job in _jobs)
        {
            var enter = EaseOut(Progress(t, job.In, 0.2));
            var exit = EaseOut(Progress(t, job.Out, 0.15));
            job.Holder.Opacity = t < job.In ? 0 : enter * (1 - exit);
            job.Rise.Y = WheelStyle.CardRise * (1 - enter) + WheelStyle.CardRise * exit;
            if (t < job.In) continue;
            if (t < job.Done) job.Card.SetProgress(Progress(t, job.In + 0.15, job.Done - job.In - 0.15));
            else if (!job.Finished)
            {
                job.Card.ShowDone([$@"C:\Users\me\Pictures\{job.Result.Name}"]);
                job.Finished = true;
            }
        }
    }

    BitmapSource Frame(double t)
    {
        Apply(t);
        _stage.UpdateLayout();
        _target.Clear();
        _target.Render(_stage);
        return _target;
    }

    // ── The desktop ─────────────────────────────────────────────────────────

    /// <summary>The still part of every frame: a soft wallpaper, the Explorer window's chrome and the taskbar.</summary>
    FrameworkElement Desktop()
    {
        var desk = new Canvas { Width = W, Height = H, ClipToBounds = true };
        desk.Children.Add(new Rectangle
        {
            Width = W,
            Height = H,
            Fill = new LinearGradientBrush(Color.FromRgb(0xEF, 0xEC, 0xE5), Color.FromRgb(0xD9, 0xD5, 0xCB), new Point(0, 0), new Point(1, 1)),
        });
        desk.Children.Add(RenderHarness.Glow(900, 250, 440, dark: false));
        var window = Explorer();
        Canvas.SetLeft(window, WinX);
        Canvas.SetTop(window, WinY);
        desk.Children.Add(window);
        var bar = TaskbarStrip();
        Canvas.SetTop(bar, WorkHeight);
        desk.Children.Add(bar);
        return desk;
    }

    FrameworkElement Explorer()
    {
        var c = _copy;
        var inside = new Canvas { Width = WinW, Height = WinH, Clip = new RectangleGeometry(new Rect(0, 0, WinW, WinH), 8, 8), Background = Hex("#FFFFFF") };

        // Tab strip: one tab for the folder, the new-tab button, the caption buttons.
        Box(inside, 0, 0, WinW, 40, "#EEEEEE");
        var tab = new Border { Width = 200, Height = 33, Background = Hex("#F9F9F9"), CornerRadius = new CornerRadius(8, 8, 0, 0) };
        Canvas.SetLeft(tab, 8);
        Canvas.SetTop(tab, 7);
        inside.Children.Add(tab);
        Place(inside, Folder(16), 20, 16);
        Label(inside, 44, 15, c.Folder, 12, "#1A1A1A");
        Glyph(inside, 186, 19, "\uE711", 9, "#5C5C5C");
        Glyph(inside, 222, 16, "\uE710", 11, "#1A1A1A");
        Glyph(inside, WinW - 136, 16, "\uE921", 10, "#1A1A1A");
        Glyph(inside, WinW - 90, 16, "\uE922", 10, "#1A1A1A");
        Glyph(inside, WinW - 44, 16, "\uE8BB", 10, "#1A1A1A");

        // Navigation and address row.
        Box(inside, 0, 40, WinW, 44, "#F9F9F9");
        Glyph(inside, 16, 55, "\uE72B", 14, "#1A1A1A");
        Glyph(inside, 50, 55, "\uE72A", 14, "#A0A0A0");
        Glyph(inside, 84, 55, "\uE74A", 14, "#1A1A1A");
        Glyph(inside, 118, 55, "\uE72C", 14, "#1A1A1A");
        Box(inside, 146, 48, 312, 28, "#FFFFFF", 4, "#E3E3E3");
        Place(inside, Folder(14), 156, 55);
        Glyph(inside, 178, 58, "\uE76C", 9, "#5C5C5C");
        Label(inside, 192, 53, c.Folder, 12, "#1A1A1A");
        Box(inside, 466, 48, WinW - 478, 28, "#FFFFFF", 4, "#E3E3E3");
        Label(inside, 478, 53, c.Search, 12, "#8A8A8A");
        Glyph(inside, WinW - 36, 56, "\uE721", 12, "#5C5C5C");

        // Command row.
        Box(inside, 0, 84, WinW, 44, "#F9F9F9");
        Glyph(inside, 16, 99, "\uE710", 13, "#1A1A1A");
        Label(inside, 36, 97, c.New, 12, "#1A1A1A");
        Line(inside, 138, 94, 138, 118, "#E0E0E0");
        var x = 154.0;
        foreach (var glyph in new[] { "\uE8C6", "\uE8C8", "\uE77F", "\uE8AC", "\uE72D", "\uE74D" })
        {
            Glyph(inside, x, 99, glyph, 14, "#5C5C5C");
            x += 36;
        }
        Line(inside, x - 4, 94, x - 4, 118, "#E0E0E0");
        Glyph(inside, x + 12, 99, "\uE8CB", 13, "#1A1A1A");
        Label(inside, x + 32, 97, c.Sort, 12, "#1A1A1A");
        Glyph(inside, x + 92, 99, "\uE80A", 13, "#1A1A1A");
        Label(inside, x + 112, 97, c.View, 12, "#1A1A1A");
        Glyph(inside, WinW - 36, 99, "\uE712", 13, "#1A1A1A");
        Line(inside, 0, BodyTop - 0.5, WinW, BodyTop - 0.5, "#E5E5E5");

        // Navigation pane, the folder selected.
        Line(inside, NavWidth + 0.5, BodyTop, NavWidth + 0.5, WinH - StatusHeight, "#EDEDED");
        string[] glyphs = ["\uE80F", "\uE91B", "\uE7F4", "\uE896", "\uE8A5", "\uE8B9", "\uE8D6", "\uE714"];
        var y = BodyTop + 10;
        for (var i = 0; i < c.Nav.Length; i++)
        {
            if (i == 2)
            {
                Line(inside, 12, y + 4, NavWidth - 12, y + 4, "#EAEAEA");
                y += 9;
            }
            var current = c.Nav[i] == c.Folder;
            if (current)
            {
                Box(inside, 6, y, NavWidth - 12, 30, "#EAEAEA", 4);
                Box(inside, 6, y + 8, 3, 14, "#0067C0", 1.5);
            }
            Glyph(inside, 20, y + 8, glyphs[i], 14, current ? "#1A1A1A" : "#5C5C5C");
            Label(inside, 44, y + 6, c.Nav[i], 12, "#1A1A1A");
            y += 32;
        }

        // Status bar (its text is drawn live, above this picture).
        Glyph(inside, WinW - 58, WinH - 18, "\uE8FD", 12, "#5C5C5C");
        Glyph(inside, WinW - 32, WinH - 18, "\uE80A", 12, "#1A1A1A");

        return new Border
        {
            Width = WinW,
            Height = WinH,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Hex("#24000000"),
            Background = Hex("#FFFFFF"),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.16 },
            Child = inside,
        };
    }

    /// <summary>A taskbar: four apps in the middle (Explorer open), the tray with Zest's icon, and the clock.</summary>
    FrameworkElement TaskbarStrip()
    {
        var bar = new Canvas { Width = W, Height = Taskbar, Background = Hex("#F2F1EE") };
        Line(bar, 0, 0.5, W, 0.5, "#DCDAD4");
        var x = W / 2 - 88;
        for (var i = 0; i < 4; i++)
        {
            if (i == 1)
            {
                Box(bar, x + 2, 4, 40, 36, "#B3FFFFFF", 4);
                Box(bar, x + 15, 38, 14, 3, "#0067C0", 1.5);
                Place(bar, Folder(22), x + 11, 12);
            }
            else Glyph(bar, x + 13, 13, i == 0 ? "\uE721" : i == 2 ? "\uE774" : "\uE713", 18, "#2B2B2B");
            x += 44;
        }

        Glyph(bar, W - 196, 17, "\uE70E", 10, "#2B2B2B");
        var tray = new Image { Source = TrayIconArt.Render(32, lightTaskbar: true), Width = 16, Height = 16 };
        Place(bar, tray, W - 172, 14);
        Glyph(bar, W - 144, 15, "\uE701", 14, "#2B2B2B");
        Glyph(bar, W - 118, 15, "\uE767", 14, "#2B2B2B");
        foreach (var (text, top) in new[] { (_copy.Time, 6.0), (_copy.Date, 22.0) })
        {
            var line = new TextBlock { Text = text, FontFamily = WheelStyle.TextFont, FontSize = 11, Foreground = Hex("#1A1A1A"), Width = 80, TextAlignment = TextAlignment.Right };
            Place(bar, line, W - 94, top);
        }
        return bar;
    }

    /// <summary>A tile in the icon grid: hover and selection box, thumbnail or document icon, and the name in up to two lines.</summary>
    void AddTile(DemoFile file, int index)
    {
        var x = GridOrigin.X + index % (int)Columns * TileStepX;
        var y = GridOrigin.Y + index / (int)Columns * TileStepY;
        var box = new Border { Width = TileW, Height = TileH, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        var art = Art(file);
        art.HorizontalAlignment = HorizontalAlignment.Center;
        art.VerticalAlignment = VerticalAlignment.Top;
        art.Margin = new Thickness(0, 8 + (66 - art.Height) / 2, 0, 0);
        var name = new TextBlock
        {
            Text = file.Name,
            FontFamily = WheelStyle.TextFont,
            FontSize = 12,
            LineHeight = 16,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            MaxHeight = 32,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Foreground = Hex("#1A1A1A"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 80, 4, 0),
        };
        var scale = new ScaleTransform(1, 1);
        var root = new Grid { Width = TileW, Height = TileH, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale, Children = { box, art, name } };
        Canvas.SetLeft(root, x);
        Canvas.SetTop(root, y);
        _tiles.Children.Add(root);
        _tileList.Add(new Tile(root, box, scale, new Rect(x, y, TileW, TileH), file));
    }

    Tile TileOf(DemoFile file) => _tileList.First(tile => tile.File == file);

    /// <summary>A file's picture: the sample landscape (88 x 55 with a hairline) or a page with a band naming its type.</summary>
    FrameworkElement Art(DemoFile file)
    {
        if (file.Photo is { } variant)
        {
            if (!_photos.TryGetValue(variant, out var photo)) _photos[variant] = photo = ToolWindows.Photo(264, 165, variant);
            return new Border
            {
                Width = 88,
                Height = 55,
                BorderThickness = new Thickness(1),
                BorderBrush = Hex("#22000000"),
                Child = new Image { Source = photo, Stretch = Stretch.UniformToFill },
            };
        }
        var extension = System.IO.Path.GetExtension(file.Name).TrimStart('.').ToUpperInvariant();
        var page = new Canvas { Width = 44, Height = 56 };
        page.Children.Add(new Path
        {
            Data = System.Windows.Media.Geometry.Parse("M0.5,0.5 L30,0.5 L43.5,14 L43.5,55.5 L0.5,55.5 Z"),
            Fill = Brushes.White,
            Stroke = Hex("#C4C4C4"),
            StrokeThickness = 1,
        });
        page.Children.Add(new Path { Data = System.Windows.Media.Geometry.Parse("M30,0.5 L30,14 L43.5,14 Z"), Fill = Hex("#E4E4E4") });
        foreach (var (top, right) in new[] { (12.0, 24.0), (18.0, 34.0), (24.0, 30.0) }) Box(page, 7, top, right - 7, 2, "#DDDDDD", 1);
        var band = new Border
        {
            Width = 34,
            Height = 14,
            CornerRadius = new CornerRadius(2),
            Background = Hex(extension == "PDF" ? "#D4473C" : "#2F6FD0"),
            Child = new TextBlock
            {
                Text = extension,
                FontFamily = WheelStyle.FormatFont,
                FontWeight = FontWeights.Bold,
                FontSize = 8.5,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Place(page, band, 5, 34);
        return page;
    }

    /// <summary>A small folder in Explorer's yellow: the back with its tab, and the front.</summary>
    static FrameworkElement Folder(double side)
    {
        var folder = new Canvas { Width = side, Height = side };
        var back = new Path
        {
            Data = System.Windows.Media.Geometry.Parse($"M0,{side * 0.18} Q0,{side * 0.1} {side * 0.08},{side * 0.1} L{side * 0.36},{side * 0.1} L{side * 0.46},{side * 0.22} L{side * 0.92},{side * 0.22} Q{side},{side * 0.22} {side},{side * 0.3} L{side},{side * 0.86} L0,{side * 0.86} Z"),
            Fill = Hex("#E6A532"),
        };
        folder.Children.Add(back);
        Box(folder, 0, side * 0.32, side, side * 0.56, "#FFC84A", side * 0.08);
        return folder;
    }

    // ── Drawing helpers ─────────────────────────────────────────────────────

    static void Place(Canvas on, FrameworkElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        on.Children.Add(element);
    }

    static void Box(Canvas on, double x, double y, double w, double h, string fill, double radius = 0, string? stroke = null) =>
        Place(on, new Border
        {
            Width = w,
            Height = h,
            Background = Hex(fill),
            CornerRadius = new CornerRadius(radius),
            BorderBrush = stroke is null ? null : Hex(stroke),
            BorderThickness = new Thickness(stroke is null ? 0 : 1),
        }, x, y);

    static void Label(Canvas on, double x, double y, string text, double size, string color) =>
        Place(on, new TextBlock { Text = text, FontFamily = WheelStyle.TextFont, FontSize = size, Foreground = Hex(color) }, x, y);

    static void Glyph(Canvas on, double x, double y, string glyph, double size, string color) =>
        Place(on, new TextBlock { Text = glyph, FontFamily = WheelStyle.IconFont, FontSize = size, Foreground = Hex(color) }, x, y);

    static void Line(Canvas on, double x1, double y1, double x2, double y2, string color) =>
        on.Children.Add(new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Hex(color), StrokeThickness = 1 });

    static SolidColorBrush Hex(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    static SolidColorBrush Hex(Argb argb) => new(WheelStyle.ToColor(argb));

    static Color Mix(Argb from, Argb to, double amount)
    {
        var a = WheelStyle.ToColor(from);
        var b = WheelStyle.ToColor(to);
        byte Lerp(byte x, byte y) => (byte)Math.Round(x + (y - x) * amount);
        return Color.FromArgb(Lerp(a.A, b.A), Lerp(a.R, b.R), Lerp(a.G, b.G), Lerp(a.B, b.B));
    }

    static double Progress(double t, double start, double duration) =>
        duration <= 0 ? (t >= start ? 1 : 0) : Math.Clamp((t - start) / duration, 0, 1);

    /// <summary>A CSS-style cubic-bezier easing: x solved by bisection, then y.</summary>
    static Func<double, double> Bezier(double x1, double y1, double x2, double y2) => t =>
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double lo = 0, hi = 1, u = t;
        for (var i = 0; i < 40; i++)
        {
            u = (lo + hi) / 2;
            if (Coordinate(u, x1, x2) < t) lo = u;
            else hi = u;
        }
        return Coordinate(u, y1, y2);
    };

    static double Coordinate(double u, double a, double b) => 3 * a * u * (1 - u) * (1 - u) + 3 * b * u * u * (1 - u) + u * u * u;

    // ── Encoding ────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the film for <paramref name="copy"/> and pipes its frames to one ffmpeg run that writes zest-demo-{code}.mp4
    /// (1920 x 1080, 30 fps, H.264) to <paramref name="videos"/> and demo-{code}.webp (960 x 540, 24 fps, looping) to
    /// <paramref name="images"/>; demo-poster-{code}.png (the convert wheel with JPG highlighted) goes beside the video.
    /// </summary>
    public static void Render(DemoCopy copy, string images, string videos, StringBuilder log)
    {
        var ffmpeg = Zest.Core.Convert.FfmpegLocator.Locate() ?? throw new InvalidOperationException("the demo needs ffmpeg on PATH");
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(videos);
        var movie = new DemoMovie(copy);
        var mp4 = System.IO.Path.GetFullPath(System.IO.Path.Combine(videos, $"zest-demo-{copy.Code}.mp4"));
        var webp = System.IO.Path.GetFullPath(System.IO.Path.Combine(images, $"demo-{copy.Code}.webp"));

        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var fps = Fps.ToString(CultureInfo.InvariantCulture);
        foreach (var arg in new[]
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{VideoWidth}x{VideoHeight}", "-r", fps, "-i", "-",
            "-c:v", "libx264", "-preset", "slow", "-crf", "16", "-pix_fmt", "yuv420p", "-movflags", "+faststart", mp4,
            "-vf", $"fps={WebpFps},scale={WebpWidth}:-2:flags=lanczos",
            "-c:v", "libwebp_anim", "-lossless", "0", "-q:v", "80", "-compression_level", "5", "-loop", "0", webp,
        })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (errors) errors.AppendLine(e.Data);
        };
        process.BeginErrorReadLine();

        var frames = (int)Math.Ceiling(movie.Duration * Fps);
        var poster = (int)Math.Round(movie.PosterAt * Fps);
        var pixels = new byte[VideoWidth * VideoHeight * 4];
        var clock = Stopwatch.StartNew();
        using (var input = process.StandardInput.BaseStream)
        {
            for (var i = 0; i < frames; i++)
            {
                var frame = movie.Frame(i / (double)Fps);
                if (i == poster) File.WriteAllBytes(System.IO.Path.Combine(videos, $"demo-poster-{copy.Code}.png"), TrayIconArt.Png(frame));
                frame.CopyPixels(pixels, VideoWidth * 4, 0);
                input.Write(pixels);
            }
        }
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg failed ({process.ExitCode}): {errors}");
        log.AppendLine(CultureInfo.InvariantCulture,
            $"demo {copy.Code}: {frames} frames ({movie.Duration:0.00} s) in {clock.Elapsed.TotalSeconds:0} s; mp4 {new FileInfo(mp4).Length} bytes, webp {new FileInfo(webp).Length} bytes");
    }
}
