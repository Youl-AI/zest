using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LimeWin.App.Shelf;
using LimeWin.App.Tools;
using LimeWin.App.Wheel;
using LimeWin.Core.Tools;
using Wpf.Ui.Controls;

namespace LimeWin.App.Render;

/// <summary>
/// Review renders of the tool windows for <c>--render</c>. A window cannot be drawn without being shown, so each scene
/// builds the real window, lifts its content out and draws it inside a stand-in frame: the Mica fallback brush, a 1 px
/// stroke and Windows 11's 8 px corners, on a plain desktop colour.
/// </summary>
internal static class ToolWindows
{
    const int PhotoW = 4000, PhotoH = 2500; // what the sample "photo" claims to be in original pixels

    public static FrameworkElement Compress(bool dark)
    {
        var window = new CompressOptionsWindow([new CompressSource(@"C:\Users\me\Pictures\제주 바다.jpg", 3_412_000)], _ => { });
        return Frame(window, dark);
    }

    public static FrameworkElement Crop(bool dark, WheelPalette palette)
    {
        var preview = Photo(1472, 920, 0);
        var window = new CropWindow(@"C:\Users\me\Pictures\제주 바다.jpg", preview, PhotoW, PhotoH, _ => { });
        window.Preview(palette, ratioIndex: 4, new PixelRect(720, 420, 2600, 1500)); // 16:9
        return Frame(window, dark);
    }

    public static FrameworkElement Order(bool dark)
    {
        var names = new[] { "여행 1.jpg", "여행 2.jpg", "여행 3.png" };
        var details = new[] { "4000 × 2500 · 3.4 MB", "3024 × 4032 · 2.8 MB", "1920 × 1080 · 1.1 MB" };
        var items = new List<OrderItem>();
        for (var i = 0; i < names.Length; i++)
        {
            var item = new OrderItem($@"C:\Users\me\Pictures\{names[i]}", isPdf: false)
            {
                Thumbnail = Photo(80, 80, i),
                Detail = details[i],
            };
            items.Add(item);
        }
        var window = new OrderWindow(OrderMode.ImagesToPdf, items, _ => { });
        return Frame(window, dark);
    }

    public static FrameworkElement TextResult(bool dark)
    {
        const string text = "── 회의록 스캔.png ──\n오늘 회의에서 논의한 내용을 정리합니다.\n1. 다음 분기 일정: 2026년 11월 3일\n2. Quarterly report 검토 담당: 김하늘\n\n── 견적서.pdf ──\n견적 번호 2026-1022\n합계 금액 1,250,000원";
        var window = new TextResultWindow([@"C:\Users\me\Documents\회의록 스캔.png", @"C:\Users\me\Documents\견적서.pdf"], text, true, _ => { });
        return Frame(window, dark);
    }

    /// <summary>The background-removal model download, 42 % through.</summary>
    public static FrameworkElement ModelDownload(bool dark)
    {
        var window = new ModelDownloadWindow(() => { });
        window.Preview(0.42);
        return Frame(window, dark);
    }

    /// <summary>A shelf holding five sample items (two selected) with Explorer's icons for their types, on a plain desktop colour.</summary>
    public static FrameworkElement Shelf(bool dark, WheelPalette palette)
    {
        (string Name, bool Folder)[] samples =
            [("여행 사진", true), ("제주 바다.jpg", false), ("회의록 2026-10.docx", false), ("견적서.pdf", false), ("bgm.mp3", false)];
        var items = samples.Select(s => new ShelfItem($@"C:\Users\me\Desktop\{s.Name}", ShellIcon.ForName(s.Name, s.Folder), missing: false));
        var window = new ShelfWindow(items, palette);
        window.Select(1, 2);
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.Resources.MergedDictionaries.Add(window.Resources); // lifted content keeps the window's styles and accent
        RenderHarness.AsOnHighDpiScreen(content);
        content.Width = window.Width;
        window.Close();
        return new Border
        {
            Padding = new Thickness(24, 24, 24, 8),
            Background = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xDD, 0xE3, 0xE8)),
            Child = content,
        };
    }

    static FrameworkElement Frame(FluentWindow window, bool dark)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.Resources.MergedDictionaries.Add(window.Resources); // lifted content keeps the window's styles
        RenderHarness.AsOnHighDpiScreen(content);

        var surface = new Border
        {
            Width = window.Width,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = content,
        };
        if (window.SizeToContent == SizeToContent.Manual) surface.Height = window.Height; // a fixed-size window, not a sized-to-content one
        surface.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorDefaultBrush");
        window.Close();

        return new Border
        {
            Padding = new Thickness(24),
            Background = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xDD, 0xE3, 0xE8)),
            Child = surface,
        };
    }

    /// <summary>A photo-like landscape (sky, sun, two mountain ranges, a lake) at a pixel size; <paramref name="variant"/> shifts the light.</summary>
    static BitmapSource Photo(int w, int h, int variant)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Color[][] skies =
            [
                [Color.FromRgb(0x3E, 0x6F, 0xB8), Color.FromRgb(0xF4, 0xC4, 0x86)],
                [Color.FromRgb(0x24, 0x3B, 0x6B), Color.FromRgb(0xE8, 0x8A, 0x6A)],
                [Color.FromRgb(0x6C, 0xA6, 0xE0), Color.FromRgb(0xDD, 0xEE, 0xF5)],
            ];
            var sky = skies[variant % skies.Length];
            var horizon = h * 0.62;
            dc.DrawRectangle(new LinearGradientBrush(sky[0], sky[1], 90), null, new Rect(0, 0, w, horizon + 1));

            var sun = new Point(w * (0.68 - 0.1 * variant), horizon * 0.55);
            var glow = new RadialGradientBrush(Color.FromArgb(0xC0, 0xFF, 0xF2, 0xC8), Color.FromArgb(0x00, 0xFF, 0xF2, 0xC8));
            dc.DrawEllipse(glow, null, sun, w * 0.16, w * 0.16);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xDC)), null, sun, w * 0.035, w * 0.035);

            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x5D, 0x74, 0x96)), null,
                Ridge(w, horizon, h * 0.22, [0.0, 0.35, 0.18, 0.62, 0.3, 0.8, 0.45, 0.55, 0.2, 0.4, 0.0]));
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x2F, 0x4A, 0x3A)), null,
                Ridge(w, horizon, h * 0.12, [0.1, 0.5, 0.3, 0.7, 0.4, 0.2, 0.6, 0.35, 0.5, 0.15]));

            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x4D, 0x6E, 0x8F), Color.FromRgb(0x1E, 0x33, 0x46), 90), null,
                new Rect(0, horizon, w, h - horizon));
            var reflection = new RadialGradientBrush(Color.FromArgb(0x70, 0xFF, 0xE9, 0xC0), Color.FromArgb(0x00, 0xFF, 0xE9, 0xC0));
            dc.DrawEllipse(reflection, null, new Point(sun.X, horizon + (h - horizon) * 0.3), w * 0.05, (h - horizon) * 0.35);
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>A mountain silhouette standing on the horizon; heights are fractions of <paramref name="peak"/>.</summary>
    static Geometry Ridge(double w, double horizon, double peak, double[] heights)
    {
        var figure = new PathFigure { StartPoint = new Point(0, horizon + 1), IsClosed = true };
        for (var i = 0; i < heights.Length; i++)
            figure.Segments.Add(new LineSegment(new Point(w * i / (heights.Length - 1), horizon - heights[i] * peak), true));
        figure.Segments.Add(new LineSegment(new Point(w, horizon + 1), true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
