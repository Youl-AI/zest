using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zest.App.Jobs;
using Zest.App.Theme;
using Zest.App.Tray;
using Zest.App.Wheel;
using Zest.Core.Catalog;
using Zest.Core.Jobs;
using Zest.Core.Tools;
using Wpf.Ui.Appearance;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace Zest.App.Render;

/// <summary>
/// <c>Zest.exe --render &lt;folder&gt;</c>: draws the real <see cref="WheelView"/>, progress cards and tray glyphs into PNGs at 2x
/// for visual review, without tray, hooks or windows. The wheel floats over a fixed backdrop like the original's screenshot
/// (dark-green window on the left, a white file list on the right), and again over plain white and black, where only the
/// petal edges and shadow keep petals apart from the page.
/// </summary>
public static class RenderHarness
{
    internal const double Scale = 2;
    const double Side = 320;

    public static int Run(string folder) => Logged(folder, log =>
    {
        var backdrop = Backdrop();
        var white = Plain(Colors.White);
        var black = Plain(Colors.Black);
        foreach (var palette in new[] { WheelStyle.LightPalette, WheelStyle.DarkPalette })
        {
            var theme = palette.Dark ? "dark" : "light";
            foreach (var (count, mode, items, highlight) in Sets())
            {
                if (items.Count != count) throw new InvalidOperationException($"expected {count} items, catalog gave {items.Count}");
                var name = $"wheel-{theme}-{ModeName(mode)}-{count}petals";
                Save(Wheel(backdrop, palette, mode, items, null), folder, $"{name}-plain.png", log);
                Save(Wheel(backdrop, palette, mode, items, highlight), folder, $"{name}-hl.png", log);
                // Worst cases for floating petals: a blank white page and a black screen behind the wheel.
                Save(Wheel(white, palette, mode, items, highlight), folder, $"{name}-on-white.png", log);
                Save(Wheel(black, palette, mode, items, highlight), folder, $"{name}-on-black.png", log);
            }
            Save(Wheel(backdrop, palette, WheelMode.Convert, [], null), folder, $"wheel-{theme}-empty.png", log);
        }

        foreach (var dark in new[] { false, true })
        {
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None, true);
            ZestTheme.Apply(dark);
            var suffix = dark ? "-dark" : "";
            foreach (var state in new[] { "running", "waiting", "done", "failed", "cancelled" })
                Save(Cards(state, dark), folder, $"progress-card-{state}{suffix}.png", log);
            Save(SummaryCard(dark), folder, $"progress-card-summary{suffix}.png", log);

            Save(ToolWindows.ZestWindowSample(dark), folder, $"zest-window{suffix}.png", log);
            Save(ToolWindows.ZestControlsSample(dark), folder, $"zest-controls{suffix}.png", log);
            Save(ToolWindows.Compress(dark, SizeGoal.Percentage), folder, $"compress-options{suffix}.png", log);
            Save(ToolWindows.Compress(dark, SizeGoal.Preset), folder, $"compress-options-preset{suffix}.png", log);
            Save(ToolWindows.Compress(dark, SizeGoal.FileSize), folder, $"compress-options-target{suffix}.png", log);
            Save(ToolWindows.Crop(dark), folder, $"crop{suffix}.png", log);
            Save(ToolWindows.Order(dark), folder, $"order{suffix}.png", log);
            Save(ToolWindows.Shelf(dark, list: false), folder, $"shelf{suffix}.png", log);
            Save(ToolWindows.Shelf(dark, list: true), folder, $"shelf-list{suffix}.png", log);
            Save(ToolWindows.TextResult(dark, copied: true), folder, $"text-result{suffix}.png", log);
            Save(ToolWindows.TextResult(dark, copied: false), folder, $"text-result-busy{suffix}.png", log);
            Save(ToolWindows.TextResult(dark, copied: false, empty: true), folder, $"text-result-empty{suffix}.png", log);
            Save(ToolWindows.ModelDownload(dark, "ask"), folder, $"model-download{suffix}.png", log);
            Save(ToolWindows.ModelDownload(dark, "progress"), folder, $"model-download-progress{suffix}.png", log);
            Save(ToolWindows.ModelDownload(dark, "failed"), folder, $"model-download-failed{suffix}.png", log);
            Save(ToolWindows.Metadata(dark), folder, $"metadata{suffix}.png", log);
            Save(ToolWindows.Metadata(dark, locationOff: true), folder, $"metadata-location-off{suffix}.png", log);
            Save(ToolWindows.Metadata(dark, wipe: true), folder, $"metadata-wipe{suffix}.png", log);
            Save(ToolWindows.MetadataBatch(dark), folder, $"metadata-batch{suffix}.png", log);
        }

        Save(TraySheet(), folder, "tray-icons.png", log);
    });

    /// <summary>
    /// Creates <paramref name="folder"/>, logs which fonts resolve, runs <paramref name="render"/> and writes
    /// <c>render.log</c> beside the images; 0 on success, 1 (the exception in the log and on stderr) on failure.
    /// </summary>
    static int Logged(string folder, Action<StringBuilder> render)
    {
        var log = new StringBuilder();
        try
        {
            Directory.CreateDirectory(folder);
            LogFonts(log);
            render(log);
            File.WriteAllText(System.IO.Path.Combine(folder, "render.log"), log.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            log.AppendLine("FAILED: " + ex);
            try { File.WriteAllText(System.IO.Path.Combine(folder, "render.log"), log.ToString()); } catch (IOException) { }
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    // ── README images (--render-readme) ─────────────────────────────────────

    /// <summary>The README's hero line, broken after the comma so it sits in the hero's left half.</summary>
    const string Tagline = "파일을 끌다가 키를 한 번 탭하면,\n그 자리에서 변환 · 도구 · 액션";

    const double HeroWidth = 1280, HeroHeight = 640, PopupsWidth = 1280, PopupsHeight = 800;
    /// <summary>The wheel images: one 320 px wheel over its backdrop, drawn at twice its size.</summary>
    const double ReadmeWheelZoom = 2;
    /// <summary>The hero's wheel: 560 of the 640 px right half, so the backdrop reads as a framed screenshot.</summary>
    const double HeroWheelZoom = 1.75;

    /// <summary>
    /// <c>Zest.exe --render-readme &lt;folder&gt;</c>: the eleven images of the README, each theme's set drawn after that
    /// theme is applied, from the real wheel, cards and windows. The wheels sit over the Explorer backdrop in the theme's
    /// Explorer colours, so a dark image has no light box in it.
    /// </summary>
    public static int RunReadme(string folder) => Logged(folder, log =>
    {
        LogHeroFonts(log);
        foreach (var dark in new[] { false, true })
        {
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None, true);
            ZestTheme.Apply(dark);
            var theme = dark ? "dark" : "light";
            var palette = WheelStyle.Palette(dark);

            Save(Hero(palette, chips: true), folder, $"hero-{theme}.png", log);
            if (!dark) Save(Hero(palette, chips: false), folder, "social-preview.png", log);
            foreach (var mode in new[] { WheelMode.Convert, WheelMode.Tools, WheelMode.Actions })
            {
                var (items, highlight) = PhotoWheel(mode);
                Save(Wheel(Backdrop(dark, ReadmeWheelZoom), palette, mode, items, highlight, ReadmeWheelZoom), folder, $"wheel-{ModeName(mode)}-{theme}.png", log);
            }
            Save(Popups(dark), folder, $"popups-{theme}.png", log);
        }
    });

    /// <summary>
    /// The wheel for photo.png in <paramref name="mode"/>, every capability on, with the README's item highlighted:
    /// JPG (convert), 메타데이터 (tools) or 선반 (actions).
    /// </summary>
    static (IReadOnlyList<WheelItem> Items, int Highlight) PhotoWheel(WheelMode mode)
    {
        var items = WheelCatalog.Items(["photo.png"], mode, Capabilities.All);
        var highlight = items.ToList().FindIndex(item => item.Operation switch
        {
            ConvertOp c => string.Equals(c.Target, "jpg", StringComparison.OrdinalIgnoreCase),
            ToolOp t => t.Tool == ZestTool.Metadata,
            ActionOp a => a.Action == DropAction.Shelf,
            _ => false,
        });
        if (highlight < 0) throw new InvalidOperationException($"the {ModeName(mode)} wheel for photo.png has no README item");
        return (items, highlight);
    }

    /// <summary>
    /// The README hero: "Zest", the tagline and (unless <paramref name="chips"/> is false, as on the social preview) the three
    /// key chips on the left; the convert wheel with JPG highlighted over the backdrop on the right, on the window colour.
    /// </summary>
    static FrameworkElement Hero(WheelPalette palette, bool chips)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(88, 0, 0, 12) };
        text.Children.Add(new TextBlock
        {
            Text = "Zest",
            FontFamily = WheelStyle.FormatFont,
            FontWeight = FontWeights.Bold,
            FontSize = 96,
            Foreground = ZestTheme.Brush("Zest.Text"),
            Margin = new Thickness(-4, 0, 0, 0), // the Z's side bearing, so the word lines up with the text under it
        });
        text.Children.Add(new TextBlock
        {
            Text = Tagline,
            FontFamily = WheelStyle.KoreanFont,
            FontWeight = WheelStyle.LabelWeight,
            FontSize = 22,
            LineHeight = 34,
            Foreground = ZestTheme.Brush("Zest.Text2"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        if (chips)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 32, 0, 0) };
            foreach (var mode in new[] { WheelMode.Convert, WheelMode.Tools, WheelMode.Actions })
                row.Children.Add(KeyChip(mode, palette.Dark));
            text.Children.Add(row);
        }

        var (items, highlight) = PhotoWheel(WheelMode.Convert);
        var side = Side * HeroWheelZoom;
        // Clipped on a wrapper: a clip on the zoomed wheel itself is in its unzoomed units.
        var shot = new Border
        {
            Child = Wheel(Backdrop(palette.Dark, HeroWheelZoom), palette, WheelMode.Convert, items, highlight, HeroWheelZoom),
            Clip = new RectangleGeometry(new Rect(0, 0, side, side), 16, 16),
        };
        var shadow = new Border { CornerRadius = new CornerRadius(16), Background = ZestTheme.Brush("Zest.Window") };
        CardShadow.Attach(shadow);
        var edge = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), BorderBrush = ZestTheme.Brush("Zest.Line") };
        var screenshot = new Grid
        {
            Width = side,
            Height = side,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, (HeroHeight - side) / 2, 0),
            Children = { shadow, shot, edge },
        };

        var hero = new Grid
        {
            Width = HeroWidth,
            Height = HeroHeight,
            Background = ZestTheme.Brush("Zest.Window"),
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() },
        };
        AsOnHighDpiScreen(hero);
        hero.Children.Add(text);
        Grid.SetColumn(screenshot, 1);
        hero.Children.Add(screenshot);
        return hero;
    }

    /// <summary>One modifier key as the wheel's hub chip shows it: the mode's ink on its wash, fully round.</summary>
    static Border KeyChip(WheelMode mode, bool dark)
    {
        var citrus = WheelStyle.Accent(mode);
        return new Border
        {
            Background = new SolidColorBrush(WheelStyle.ToColor(citrus.Wash(dark))),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16, 7, 16, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = WheelStyle.ModeChip(mode),
                FontFamily = WheelStyle.ChipFont,
                FontWeight = WheelStyle.FormatWeight,
                FontSize = 18,
                Foreground = new SolidColorBrush(WheelStyle.ToColor(citrus.Ink(dark))),
            },
        };
    }

    /// <summary>
    /// The popups: a progress card stack (a running and a finished conversion, a running tool), the compress window and the
    /// shelf side by side on the stage colour (<c>Zest.Sunken</c> on light, the window colour on dark). The helpers draw each
    /// on the review renders' desktop colour; here that background is cleared so the stage shows behind every shadow.
    /// </summary>
    static FrameworkElement Popups(bool dark)
    {
        // The cards keep 14 px of shadow room a side, the windows about twice that: 20 more on the right evens out the gaps.
        var cards = new StackPanel { Margin = new Thickness(0, 14, 20, 4) };
        var converting = new ProgressCard("PNG 변환", "사진.jpg", JobKind.Convert, () => { });
        converting.SetProgress(0.42);
        var converted = new ProgressCard("JPG 변환", "스크린샷.png", JobKind.Convert, () => { });
        converted.ShowDone([@"C:\Users\me\Pictures\스크린샷.jpg"]);
        var compressing = new ProgressCard("압축", "제주 바다.jpg", JobKind.Tool, () => { });
        compressing.SetProgress(0.7);
        cards.Children.Add(converting);
        cards.Children.Add(converted);
        cards.Children.Add(compressing);
        AsOnHighDpiScreen(cards);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var popup in new[] { cards, Unstaged(ToolWindows.Compress(dark, SizeGoal.Percentage)), Unstaged(ToolWindows.Shelf(dark, list: false)) })
        {
            popup.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(popup);
        }

        return new Grid
        {
            Width = PopupsWidth,
            Height = PopupsHeight,
            Background = ZestTheme.Brush(dark ? "Zest.Window" : "Zest.Sunken"),
            Children = { row },
        };
    }

    /// <summary>Clears the plain desktop colour a review render is laid on, leaving the popup and its shadow.</summary>
    static FrameworkElement Unstaged(FrameworkElement render)
    {
        switch (render)
        {
            case Border border: border.Background = null; break;
            case Panel panel: panel.Background = null; break;
        }
        return render;
    }

    /// <summary>Logs the typefaces the hero's own text resolves to: the title's Latin face and the tagline's Pretendard.</summary>
    static void LogHeroFonts(StringBuilder log)
    {
        LogFont(log, "hero-title", WheelStyle.FormatFont, FontWeights.Bold, 'Z');
        LogFont(log, "hero-tagline", WheelStyle.KoreanFont, WheelStyle.LabelWeight, '탭');
    }

    /// <summary>
    /// Petal counts with real catalog items, each drawn in the citrus of the wheel it comes from. 9 is a stress set (all tools
    /// plus three actions) since no single wheel has that many, so it is drawn as both the tools and the actions wheel.
    /// </summary>
    static IEnumerable<(int Count, WheelMode Mode, IReadOnlyList<WheelItem> Items, int Highlight)> Sets()
    {
        var noOcr = Capabilities.All with { Ocr = false, BackgroundRemoval = false };
        var ocr = Capabilities.All with { BackgroundRemoval = false };
        yield return (2, WheelMode.Tools, WheelCatalog.Items(["report.pdf"], WheelMode.Tools, noOcr), 0); // split + metadata
        yield return (5, WheelMode.Tools, WheelCatalog.Items(["photo.png"], WheelMode.Tools, ocr), 2); // "PDF 만들기": the longest hub name
        yield return (5, WheelMode.Actions, WheelCatalog.Items(["photo.png"], WheelMode.Actions, Capabilities.All), 2);
        yield return (7, WheelMode.Convert, WheelCatalog.Items(["photo.png"], WheelMode.Convert, Capabilities.All), 1);
        var nine = WheelCatalog.Items(["photo.png"], WheelMode.Tools, Capabilities.All)
            .Concat(WheelCatalog.Items(["photo.png"], WheelMode.Actions, Capabilities.All).Take(3)).ToList();
        yield return (9, WheelMode.Tools, nine, 3);
        yield return (9, WheelMode.Actions, nine, 3);
    }

    static string ModeName(WheelMode mode) => mode switch
    {
        WheelMode.Convert => "convert",
        WheelMode.Tools => "tools",
        _ => "actions",
    };

    /// <summary>
    /// The wheel over <paramref name="backdrop"/>; a <paramref name="zoom"/> above 1 enlarges both (the wheel as vectors,
    /// so the backdrop must have been drawn at the same zoom to stay sharp).
    /// </summary>
    static FrameworkElement Wheel(BitmapSource backdrop, WheelPalette palette, WheelMode mode, IReadOnlyList<WheelItem> items, int? highlight, double zoom = 1)
    {
        var view = new WheelView { Animate = false, Palette = palette, Mode = mode };
        AsOnHighDpiScreen(view);
        if (items.Count == 0) view.ShowEmpty();
        else view.SetItems(items);
        view.Highlight(highlight);

        var root = new Grid { Width = Side, Height = Side };
        if (zoom != 1) root.LayoutTransform = new ScaleTransform(zoom, zoom);
        root.Children.Add(new Image { Source = backdrop, Width = Side, Height = Side, Stretch = Stretch.Fill });
        root.Children.Add(view);
        return root;
    }

    /// <summary>
    /// Stand-in for the screen behind the wheel, rendered at 2x like a real capture on a 200 % display: a file list (white,
    /// or Explorer's dark list when <paramref name="dark"/>) half covered by a dark-green window. <paramref name="zoom"/>
    /// draws it that many times larger, for a wheel enlarged by the same factor.
    /// </summary>
    static BitmapSource Backdrop(bool dark = false, double zoom = 1)
    {
        var canvas = new Canvas { Width = Side, Height = Side, Background = dark ? new SolidColorBrush(Color.FromRgb(0x19, 0x19, 0x19)) : Brushes.White };
        for (var i = 0; i < 23; i++)
        {
            var line = new TextBlock
            {
                Text = $"Report final v{29 + i}.docx",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = dark ? Brushes.White : Brushes.Black,
            };
            Canvas.SetLeft(line, 96);
            Canvas.SetTop(line, -8 + i * 15);
            canvas.Children.Add(line);
        }

        // A dark-green window covering the left of the list, as in the original's screenshot.
        var panel = new Grid { Width = 124, Height = Side };
        panel.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromRgb(0x3B, 0x5E, 0x1E), Color.FromRgb(0x2B, 0x6E, 0x66), 90),
        });
        panel.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(
                [new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0x55, 0x05, 0x1A, 0x08), 0.55), new GradientStop(Color.FromArgb(0x20, 0, 0, 0), 1)],
                0),
        });
        canvas.Children.Add(panel);
        if (zoom == 1) return Snapshot(canvas, Side, Side);
        canvas.LayoutTransform = new ScaleTransform(zoom, zoom);
        AsOnHighDpiScreen(canvas); // Display text is hinted to 96 DPI pixels, which the zoom would then stretch
        return Snapshot(canvas, Side * zoom, Side * zoom);
    }

    static BitmapSource Plain(Color color) =>
        Snapshot(new Border { Width = Side, Height = Side, Background = new SolidColorBrush(color) }, Side, Side);

    /// <summary>
    /// One state as the host stacks it: a convert (lemon), a tool (tangerine) and an action (lime) card, top to bottom.
    /// The tool card carries a long Korean name, which must end in an ellipsis without pushing 취소 off the card, and an
    /// error longer than three lines, which must stop at the third with an ellipsis.
    /// </summary>
    static FrameworkElement Cards(string state, bool dark)
    {
        const string Pictures = @"C:\Users\me\Pictures\";
        const string LongName = "2026 제주 가족 여행 바다 사진 원본.jpg";
        (string Title, string Detail, JobKind Kind, double? Progress, string[] Results, string Error)[] jobs =
        [
            ("PNG 변환", "사진.jpg", JobKind.Convert, 0.42, [Pictures + "사진.png"],
                "메모.png를 이미지로 읽을 수 없습니다. 확장자만 .png인 파일일 수 있습니다."),
            ("압축", LongName, JobKind.Tool, 0.7, [Pictures + "2026 제주 가족 여행 바다 사진 원본 (compressed).jpg"],
                "목표 크기까지 줄이지 못했습니다. 품질을 가장 낮춰도 2 MB보다 큽니다. 크기를 조금 더 크게 잡거나 해상도를 줄여 보세요. 원본은 그대로 있고, 만들던 파일은 지웠습니다. 다시 시도하려면 파일을 휠에 다시 놓으세요."),
            ("이동", "보고서.pdf 외 2개", JobKind.Action, null, [@"D:\보관\보고서.pdf", @"D:\보관\견적서.pdf", @"D:\보관\메모.txt"],
                "클립보드를 쓸 수 없습니다. 다른 프로그램이 사용 중입니다."),
        ];
        var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 4) };
        foreach (var job in jobs)
        {
            var card = new ProgressCard(job.Title, job.Detail, job.Kind, () => { });
            switch (state)
            {
                case "running":
                    if (job.Progress is { } p) card.SetProgress(p); // the action stays indeterminate (no percent)
                    break;
                case "waiting":
                    card.SetWaiting(true);
                    break;
                case "done":
                    card.ShowDone(job.Results);
                    break;
                case "failed":
                    card.ShowFailed(job.Error);
                    break;
                case "cancelled":
                    card.ShowCancelled();
                    break;
            }
            stack.Children.Add(card);
        }
        return OnDesktop(stack, dark);
    }

    /// <summary>The "외 N개 작업" card under a running one, as the host shows it past six cards.</summary>
    static FrameworkElement SummaryCard(bool dark)
    {
        var card = new ProgressCard("MKV 변환", "영상 3.mp4", JobKind.Convert, () => { });
        card.SetProgress(0.18);
        var (summary, text) = ProgressCardHost.BuildSummary(() => { });
        summary.Visibility = Visibility.Visible;
        text.Text = "외 4개 작업";
        return OnDesktop(new StackPanel { Margin = new Thickness(0, 14, 0, 4), Children = { card, summary } }, dark);
    }

    /// <summary>A plain desktop-ish surface so the card's edge and shadow read as they would on screen.</summary>
    static FrameworkElement OnDesktop(FrameworkElement content, bool dark)
    {
        AsOnHighDpiScreen(content);
        var root = new Grid { Background = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xDD, 0xE3, 0xE8)) };
        root.Children.Add(content);
        return root;
    }

    /// <summary>Each tray size at 1:1 and enlarged 4x (nearest neighbour), on a light and a dark taskbar strip.</summary>
    static FrameworkElement TraySheet()
    {
        var sheet = new StackPanel();
        foreach (var light in new[] { true, false })
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush(light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20)),
            };
            foreach (var size in TrayIconArt.Sizes)
            {
                var art = TrayIconArt.Render(size, light);
                row.Children.Add(Pixel(art, 1, new Thickness(12, 12, 6, 12)));
                row.Children.Add(Pixel(art, 4, new Thickness(6, 12, 12, 12)));
            }
            sheet.Children.Add(row);
        }
        return sheet;
    }

    static Image Pixel(BitmapSource art, double zoom, Thickness margin)
    {
        // Device pixels at 2x output: one icon pixel = Scale * zoom output pixels.
        var image = new Image { Source = art, Width = art.PixelWidth * zoom / Scale, Height = art.PixelHeight * zoom / Scale, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    /// <summary>
    /// The views use Display text formatting, which on screen hints glyphs to the monitor's real pixels. RenderTargetBitmap
    /// instead rasterizes Display text at 96 DPI and stretches it to 2x; Ideal is what a 200 % screen effectively shows.
    /// </summary>
    internal static void AsOnHighDpiScreen(FrameworkElement element) =>
        TextOptions.SetTextFormattingMode(element, TextFormattingMode.Ideal);

    static void Save(FrameworkElement element, string folder, string name, StringBuilder log)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = element.DesiredSize;
        var png = TrayIconArt.Png(Snapshot(element, size.Width, size.Height));
        var path = System.IO.Path.Combine(folder, name);
        File.WriteAllBytes(path, png);
        log.AppendLine(CultureInfo.InvariantCulture, $"{name}: {size.Width * Scale}x{size.Height * Scale}");
    }

    internal static BitmapSource Snapshot(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Which font files the wheel's typefaces actually resolve to (a missing font falls back silently in WPF).</summary>
    static void LogFonts(StringBuilder log)
    {
        LogFont(log, "format", WheelStyle.FormatFont, WheelStyle.LabelWeight, 'W');
        LogFont(log, "korean", WheelStyle.KoreanFont, WheelStyle.LabelWeight, '압');
        LogFont(log, "icons", WheelStyle.IconFont, FontWeights.Normal, '\uE7A8');
    }

    static void LogFont(StringBuilder log, string name, FontFamily family, FontWeight weight, char probe)
    {
        var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
        if (typeface.TryGetGlyphTypeface(out var glyphs))
            log.AppendLine(CultureInfo.InvariantCulture, $"{name}: {glyphs.FontUri} weight={glyphs.Weight} has '{probe}'={glyphs.CharacterToGlyphMap.ContainsKey(probe)}");
        else
            log.AppendLine($"{name}: NOT RESOLVED");
    }
}
