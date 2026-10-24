using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zest.App.Shelf;
using Zest.App.Theme;
using Zest.App.Tools;
using Zest.App.Wheel;
using Zest.Core.Tools;
using Wpf.Ui.Controls;

namespace Zest.App.Render;

/// <summary>
/// Review renders of the tool windows for <c>--render</c>. A window cannot be drawn without being shown, so each scene
/// builds the real <see cref="ZestWindow"/> and draws its frame as it is (shadow margin included) on a plain desktop colour.
/// </summary>
internal static class ToolWindows
{
    const int PhotoW = 4000, PhotoH = 2500; // what the sample "photo" claims to be in original pixels

    /// <summary>The compress options in one mode; the target-size scene also shows an error on the bottom bar.</summary>
    public static FrameworkElement Compress(bool dark, SizeGoal goal)
    {
        var window = new CompressOptionsWindow([new CompressSource(@"C:\Users\me\Pictures\제주 바다.jpg", 5_600_000)], _ => { });
        if (goal == SizeGoal.FileSize) window.Preview(goal, "9", "목표 크기는 원본보다 작아야 합니다.");
        else window.Preview(goal);
        return Shot(window, dark);
    }

    public static FrameworkElement Crop(bool dark)
    {
        var preview = Photo(1472, 920, 0);
        var window = new CropWindow(@"C:\Users\me\Pictures\제주 바다.jpg", preview, PhotoW, PhotoH, _ => { });
        window.Preview(ratioIndex: 4, new PixelRect(720, 420, 2600, 1500)); // 16:9
        return Shot(window, dark);
    }

    /// <summary>Four images (the last with a long Korean name), the first selected.</summary>
    public static FrameworkElement Order(bool dark)
    {
        var names = new[] { "여행 1.jpg", "여행 2.jpg", "여행 3.png", "2026 제주 가족 여행 바다 사진 원본 (보정 전).jpg" };
        var details = new[] { "4000 × 2500 · 3.4 MB", "3024 × 4032 · 2.8 MB", "1920 × 1080 · 1.1 MB", "4032 × 3024 · 5.6 MB" };
        var items = new List<OrderItem>();
        for (var i = 0; i < names.Length; i++)
        {
            var item = new OrderItem($@"C:\Users\me\Pictures\{names[i]}", isPdf: false)
            {
                Thumbnail = Photo(120, 90, i),
                Detail = details[i],
            };
            items.Add(item);
        }
        var window = new OrderWindow(OrderMode.ImagesToPdf, items, _ => { });
        return Shot(window, dark);
    }

    /// <summary>
    /// The text right after it was copied (the chip), with the clipboard busy (the reason under the text), or nothing found
    /// (<paramref name="empty"/>: the message, 텍스트로 저장 and 복사 disabled).
    /// </summary>
    public static FrameworkElement TextResult(bool dark, bool copied, bool empty = false)
    {
        var text = empty ? "" : "── 회의록 스캔.png ──\n오늘 회의에서 논의한 내용을 정리합니다.\n1. 다음 분기 일정: 2026년 11월 3일\n2. Quarterly report 검토 담당: 김하늘\n\n── 견적서.pdf ──\n견적 번호 2026-1022\n합계 금액 1,250,000원";
        var window = new TextResultWindow([@"C:\Users\me\Documents\회의록 스캔.png", @"C:\Users\me\Documents\견적서.pdf"], text, copied, _ => { });
        return Shot(window, dark);
    }

    /// <summary>The background-removal model download: "ask" (before 내려받기), "progress" (42 % through) or "failed".</summary>
    public static FrameworkElement ModelDownload(bool dark, string state)
    {
        var window = new ModelDownloadWindow(() => { });
        if (state == "progress") window.Preview(0.42);
        else if (state == "failed") window.PreviewFailed("모델을 내려받지 못했습니다. 인터넷 연결을 확인한 뒤 다시 시도하세요.");
        return Shot(window, dark);
    }

    /// <summary>The bare <see cref="ZestWindow"/> frame: heading "압축", a two-line body and a footer with a hint, 취소 and 압축.</summary>
    public static FrameworkElement ZestWindowSample(bool dark)
    {
        var body = new StackPanel { Margin = new Thickness(20, 6, 20, 16) };
        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "제주 바다.jpg",
            FontFamily = WheelStyle.TextFont,
            FontSize = 13,
            Foreground = ZestTheme.Brush("Zest.Text2"),
        });
        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "원본 크기 5.6 MB",
            Margin = new Thickness(0, 4, 0, 0),
            FontFamily = WheelStyle.ChipFont,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = ZestTheme.Brush("Zest.Text"),
        });

        var footer = new DockPanel { Style = Resource<Style>("Zest.FooterBar") };
        footer.Children.Add(new Wpf.Ui.Controls.Button { Content = "압축", Appearance = ControlAppearance.Primary, IsDefault = true });
        footer.Children.Add(new Wpf.Ui.Controls.Button { Content = "취소", IsCancel = true });
        footer.Children.Add(new System.Windows.Controls.TextBlock { Text = "옆에 ‘제주 바다 (compressed).jpg’로 저장" });

        var window = new ZestWindow
        {
            Heading = "압축",
            Width = 400 + 2 * ZestWindow.ShadowMargin,
            SizeToContent = SizeToContent.Height,
            Animate = false,
            Content = body,
            Footer = footer,
        };
        return Shot(window, dark);
    }

    /// <summary>
    /// The shared controls on a frame: segments, choice chips, a field (one focused-looking), a status chip and a ghost
    /// button, so their states can be checked against the popup page.
    /// </summary>
    public static FrameworkElement ZestControlsSample(bool dark)
    {
        var body = new StackPanel { Margin = new Thickness(20, 6, 20, 16) };

        var segments = new UniformGrid { Rows = 1 };
        foreach (var (label, on) in new[] { ("프리셋", false), ("목표 크기", false), ("비율", true) })
            segments.Children.Add(new RadioButton { Content = label, GroupName = "seg", IsChecked = on, Style = Resource<Style>("Zest.Segment") });
        body.Children.Add(new Border { Style = Resource<Style>("Zest.SegmentPanel"), Child = segments });

        body.Children.Add(new Border { Style = Resource<Style>("Zest.Hairline"), Margin = new Thickness(0, 14, 0, 14) });

        var chips = new WrapPanel();
        foreach (var (label, on) in new[] { ("자유", false), ("1:1", false), ("4:3", false), ("16:9", true), ("원본", false) })
            chips.Children.Add(new RadioButton { Content = label, GroupName = "chip", IsChecked = on, Margin = new Thickness(0, 0, 4, 0), Style = Resource<Style>("Zest.ChoiceChip") });
        body.Children.Add(chips);

        var fields = new UniformGrid { Rows = 1, Margin = new Thickness(0, 12, 0, 0) };
        var plain = new System.Windows.Controls.TextBox { Text = "2,5", Margin = new Thickness(0, 0, 6, 0), Style = Resource<Style>("Zest.Field") };
        var focused = new System.Windows.Controls.TextBox { Text = "1920", Style = Resource<Style>("Zest.Field") };
        focused.SetResourceReference(Control.BorderBrushProperty, "Zest.Tangerine.Ink"); // stands in for keyboard focus
        fields.Children.Add(plain);
        fields.Children.Add(focused);
        body.Children.Add(fields);

        var chip = new Border { Style = Resource<Style>("Zest.Chip"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        chip.SetResourceReference(Border.BackgroundProperty, "Zest.Lime.Wash");
        var chipText = new System.Windows.Controls.TextBlock { Text = "클립보드에 복사함", Style = Resource<Style>("Zest.ChipText") };
        chipText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Zest.Lime.Ink");
        chip.Child = chipText;
        body.Children.Add(chip);

        var footer = new DockPanel { Style = Resource<Style>("Zest.FooterBar") };
        footer.Children.Add(new Wpf.Ui.Controls.Button { Content = "복사", Appearance = ControlAppearance.Primary });
        footer.Children.Add(new Wpf.Ui.Controls.Button { Content = "텍스트로 저장" });
        footer.Children.Add(new System.Windows.Controls.Button { Content = "닫기", Style = Resource<Style>("Zest.GhostButton") });
        footer.Children.Add(new CheckBox { Content = "다시 묻기", IsChecked = true });

        var window = new ZestWindow
        {
            Heading = "자르기 · 2026 제주 가족 여행 바다 사진 원본 (보정 전).jpg", // too long: must end in an ellipsis, close in place
            Width = 400 + 2 * ZestWindow.ShadowMargin,
            SizeToContent = SizeToContent.Height,
            Animate = false,
            Content = body,
            Footer = footer,
        };
        return Shot(window, dark);
    }

    static T Resource<T>(string key) => (T)Application.Current.FindResource(key);

    /// <summary>
    /// Draws a <see cref="ZestWindow"/> as it is, shadow margin included, without showing it. An unshown window measures
    /// to nothing, so its template root (what fills the HWND) is measured at the window's width, as
    /// <see cref="ZestWindow"/> does for <c>SizeToContent</c>, drawn with its transparent margin, and laid on the plain
    /// desktop colour.
    /// </summary>
    static FrameworkElement Shot(ZestWindow window, bool dark)
    {
        RenderHarness.AsOnHighDpiScreen(window);
        window.ApplyTemplate();
        var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        // A fixed-height window (글자 복사) is drawn at its height; a sized-to-content one at what its frame measures.
        var fixedHeight = !window.SizeToContent.HasFlag(SizeToContent.Height) && !double.IsNaN(window.Height);
        root.Measure(new Size(window.Width, fixedHeight ? window.Height : double.PositiveInfinity));
        var size = new Size(window.Width, fixedHeight ? window.Height : Math.Ceiling(root.DesiredSize.Height));
        root.Arrange(new Rect(size));
        root.UpdateLayout(); // raises the size notifications that ActualHeight bindings (round chips) wait for
        var picture = new RenderTargetBitmap((int)Math.Ceiling(size.Width * RenderHarness.Scale), (int)Math.Ceiling(size.Height * RenderHarness.Scale),
            96 * RenderHarness.Scale, 96 * RenderHarness.Scale, PixelFormats.Pbgra32);
        picture.Render(root);
        picture.Freeze();
        window.Close();
        return new Border
        {
            Background = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xDD, 0xE3, 0xE8)),
            Child = new System.Windows.Controls.Image { Source = picture, Width = size.Width, Height = size.Height },
        };
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
