using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LimeWin.App.Jobs;
using LimeWin.App.Tray;
using LimeWin.App.Wheel;
using LimeWin.Core.Catalog;
using Wpf.Ui.Appearance;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace LimeWin.App.Render;

/// <summary>
/// <c>LimeWin.App.exe --render &lt;folder&gt;</c>: draws the real <see cref="WheelView"/>, progress cards and tray glyphs into PNGs at 2x
/// for visual review, without tray, hooks or windows. The glass sits on a fixed backdrop like the original's screenshot
/// (dark-green window on the left, a white file list on the right) so the frosting is visible.
/// </summary>
public static class RenderHarness
{
    const double Scale = 2;
    const double Side = 320;

    public static int Run(string folder)
    {
        var log = new StringBuilder();
        try
        {
            Directory.CreateDirectory(folder);
            LogFonts(log);
            var backdrop = Backdrop();
            foreach (var palette in new[] { WheelStyle.LightPalette, WheelStyle.DarkPalette })
            {
                var theme = palette.Dark ? "dark" : "light";
                foreach (var (count, items, highlight) in Sets())
                {
                    if (items.Count != count) throw new InvalidOperationException($"expected {count} items, catalog gave {items.Count}");
                    Save(Wheel(backdrop, palette, items, null), folder, $"wheel-{theme}-{count}petals-plain.png", log);
                    Save(Wheel(backdrop, palette, items, highlight), folder, $"wheel-{theme}-{count}petals-hl.png", log);
                }
                Save(Wheel(backdrop, palette, [], null), folder, $"wheel-{theme}-empty.png", log);

                // Worst cases for the glass: a blank white page and a black screen behind the wheel.
                var seven = Sets().First(s => s.Count == 7);
                Save(Wheel(Plain(Colors.White), palette, seven.Items, seven.Highlight), folder, $"wheel-{theme}-7petals-hl-on-white.png", log);
                Save(Wheel(Plain(Colors.Black), palette, seven.Items, seven.Highlight), folder, $"wheel-{theme}-7petals-hl-on-black.png", log);
            }

            foreach (var dark in new[] { false, true })
            {
                ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None, true);
                var suffix = dark ? "-dark" : "";
                Save(Card("running", dark), folder, $"progress-card-running{suffix}.png", log);
                Save(Card("done", dark), folder, $"progress-card-done{suffix}.png", log);
                Save(Card("failed", dark), folder, $"progress-card-failed{suffix}.png", log);

                var palette = dark ? WheelStyle.DarkPalette : WheelStyle.LightPalette;
                Save(ToolWindows.Compress(dark), folder, $"compress-options{suffix}.png", log);
                Save(ToolWindows.Crop(dark, palette), folder, $"crop{suffix}.png", log);
                Save(ToolWindows.Order(dark), folder, $"order{suffix}.png", log);
            }

            Save(TraySheet(), folder, "tray-icons.png", log);
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

    /// <summary>Petal counts with real catalog items; 9 is a stress set (all tools plus four actions) since no single wheel has that many.</summary>
    static IEnumerable<(int Count, IReadOnlyList<WheelItem> Items, int Highlight)> Sets()
    {
        var noOcr = Capabilities.All with { Ocr = false, BackgroundRemoval = false };
        var ocr = Capabilities.All with { BackgroundRemoval = false };
        yield return (1, WheelCatalog.Items(["report.pdf"], WheelMode.Tools, noOcr), 0);
        yield return (4, WheelCatalog.Items(["photo.png"], WheelMode.Tools, ocr), 1);
        yield return (5, WheelCatalog.Items(["photo.png"], WheelMode.Actions, Capabilities.All), 2);
        yield return (7, WheelCatalog.Items(["photo.png"], WheelMode.Convert, Capabilities.All), 1);
        var nine = WheelCatalog.Items(["photo.png"], WheelMode.Tools, Capabilities.All)
            .Concat(WheelCatalog.Items(["photo.png"], WheelMode.Actions, Capabilities.All).Take(4)).ToList();
        yield return (9, nine, 3);
    }

    static FrameworkElement Wheel(BitmapSource backdrop, WheelPalette palette, IReadOnlyList<WheelItem> items, int? highlight)
    {
        var view = new WheelView { Animate = false, Palette = palette };
        AsOnHighDpiScreen(view);
        view.SetBackdrop(backdrop);
        if (items.Count == 0) view.ShowEmpty();
        else view.SetItems(items);
        view.Highlight(highlight);

        var root = new Grid { Width = Side, Height = Side };
        root.Children.Add(new Image { Source = backdrop, Width = Side, Height = Side, Stretch = Stretch.Fill });
        root.Children.Add(view);
        return root;
    }

    /// <summary>Stand-in for the screen behind the wheel, rendered at 2x like a real capture on a 200 % display.</summary>
    static BitmapSource Backdrop()
    {
        var canvas = new Canvas { Width = Side, Height = Side, Background = Brushes.White };
        for (var i = 0; i < 23; i++)
        {
            var line = new TextBlock
            {
                Text = $"Report final v{29 + i}.docx",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = Brushes.Black,
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
        return Snapshot(canvas, Side, Side);
    }

    static BitmapSource Plain(Color color) =>
        Snapshot(new Border { Width = Side, Height = Side, Background = new SolidColorBrush(color) }, Side, Side);

    static FrameworkElement Card(string state, bool dark)
    {
        var card = new ProgressCard(state == "failed" ? "scan.heic → JPG" : "vacation-photo.png → JPG", () => { });
        AsOnHighDpiScreen(card);
        switch (state)
        {
            case "running":
                card.SetProgress(0.42);
                break;
            case "done":
                card.ShowDone([@"C:\Users\me\Pictures\vacation-photo.jpg"]);
                break;
            default:
                card.ShowFailed("이미지를 읽을 수 없습니다.");
                break;
        }

        // A plain desktop-ish surface so the card's edge and shadow read as they would on screen.
        var root = new Grid { Background = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xDD, 0xE3, 0xE8)) };
        root.Children.Add(new Border { Padding = new Thickness(0, 12, 0, 0), Child = card });
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
        void Check(string name, FontFamily family, FontWeight weight, char probe)
        {
            var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            if (typeface.TryGetGlyphTypeface(out var glyphs))
                log.AppendLine(CultureInfo.InvariantCulture, $"{name}: {glyphs.FontUri} weight={glyphs.Weight} has '{probe}'={glyphs.CharacterToGlyphMap.ContainsKey(probe)}");
            else
                log.AppendLine($"{name}: NOT RESOLVED");
        }
        Check("format", WheelStyle.FormatFont, WheelStyle.LabelWeight, 'W');
        Check("korean", WheelStyle.KoreanFont, WheelStyle.LabelWeight, '압');
        Check("icons", WheelStyle.IconFont, FontWeights.Normal, '\uE7A8');
    }
}
