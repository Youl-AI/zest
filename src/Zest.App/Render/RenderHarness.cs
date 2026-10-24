using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zest.App.Jobs;
using Zest.App.Theme;
using Zest.App.Tray;
using Zest.App.Wheel;
using Zest.Core.Catalog;
using Zest.Core.Design;
using Zest.Core.Jobs;
using Zest.Core.Tools;
using Zest.Core.Wheel;
using Wpf.Ui.Appearance;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace Zest.App.Render;

/// <summary>
/// <c>Zest.exe --render &lt;folder&gt;</c>: draws the real <see cref="WheelView"/>, progress cards and tray glyphs into PNGs at 2x
/// for visual review, without tray, hooks or windows. The wheel floats over a fixed backdrop that stands in for a busy
/// screen (dark-green window on the left, a white file list on the right), and again over plain white and black, where only the
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
            foreach (var (count, mode, items, highlight, summary) in Sets())
            {
                if (items.Count != count) throw new InvalidOperationException($"expected {count} items, catalog gave {items.Count}");
                var name = $"wheel-{theme}-{ModeName(mode)}-{count}petals";
                Save(Wheel(backdrop, palette, mode, items, null, summary), folder, $"{name}-plain.png", log);
                Save(Wheel(backdrop, palette, mode, items, highlight, summary), folder, $"{name}-hl.png", log);
                // Worst cases for floating petals: a blank white page and a black screen behind the wheel.
                Save(Wheel(white, palette, mode, items, highlight, summary), folder, $"{name}-on-white.png", log);
                Save(Wheel(black, palette, mode, items, highlight, summary), folder, $"{name}-on-black.png", log);
            }
            Save(Wheel(backdrop, palette, WheelMode.Convert, [], null, ""), folder, $"wheel-{theme}-empty.png", log);
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

    /// <summary>The header's one line under "Zest", per README language.</summary>
    const string TaglineKo = "끌던 파일을, 그 자리에서 바로.", TaglineEn = "Handle files mid-drag, right where they are.";

    /// <summary>Header and wheels images: 1280 x 480 DIP, 2560 x 960 px. Social preview: 640 x 320 DIP, 1280 x 640 px.</summary>
    const double BannerWidth = 1280, BannerHeight = 480, SocialWidth = 640, SocialHeight = 320, PopupsWidth = 1280;
    /// <summary>Demo frames: 640 x 360 DIP, 1280 x 720 px.</summary>
    const double DemoWidth = 640, DemoHeight = 360;
    /// <summary>Stage above and below the tallest popup's drawn bounds; its own shadow room comes on top of this.</summary>
    const double PopupsPad = 32;
    /// <summary>The demo's highlight steps run at 2 frames a second.</summary>
    const int DemoFps = 2;

    static readonly WheelMode[] Modes = [WheelMode.Convert, WheelMode.Tools, WheelMode.Actions];

    /// <summary>
    /// <c>Zest.exe --render-readme &lt;folder&gt;</c>: the README's images, drawn from the real wheel, cards and windows on
    /// plain stages (no stand-in screenshot behind them): the app mark (icon-1024.png), the Korean and English headers, the
    /// social preview, the three wheels side by side, the popups, and the demo animation (demo.webp, assembled by ffmpeg
    /// from PNG frames when ffmpeg is on PATH; demo-poster.png is its first frame).
    /// </summary>
    public static int RunReadme(string folder) => Logged(folder, log =>
    {
        LogHeaderFonts(log);
        foreach (var dark in new[] { false, true })
        {
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None, true);
            ZestTheme.Apply(dark);
            var theme = dark ? "dark" : "light";

            Save(Header(TaglineKo, dark), folder, $"header-ko-{theme}.png", log);
            Save(Header(TaglineEn, dark), folder, $"header-en-{theme}.png", log);
            Save(Wheels(dark), folder, $"wheels-{theme}.png", log);
            Save(Popups(dark), folder, $"popups-{theme}.png", log);
            if (dark) continue;
            Save(Social(), folder, "social-preview.png", log);
            Save(Icon(), folder, "icon-1024.png", log);
            Demo(folder, log);
        }
    });

    /// <summary>
    /// The wheel for photo.png in <paramref name="mode"/>, every capability on, and the index of each petal named in
    /// <paramref name="match"/> order (format targets for convert, tools, actions).
    /// </summary>
    static (IReadOnlyList<WheelItem> Items, int[] Steps) PhotoWheel(WheelMode mode, params object[] match)
    {
        var items = WheelCatalog.Items(["photo.png"], mode, Capabilities.All);
        var steps = match.Select(want =>
        {
            var at = items.ToList().FindIndex(item => (item.Operation, want) switch
            {
                (ConvertOp c, string target) => string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase),
                (ToolOp t, ZestTool tool) => t.Tool == tool,
                (ActionOp a, DropAction action) => a.Action == action,
                _ => false,
            });
            if (at < 0) throw new InvalidOperationException($"the {ModeName(mode)} wheel for photo.png has no {want} petal");
            return at;
        }).ToArray();
        return (items, steps);
    }

    /// <summary>The sample photo.png the README wheels are drawn for, as its hub summary shows it (5.6 MB).</summary>
    static readonly string PhotoSummary = HubSummary.For([new HubEntry(IsFolder: false, 5_600_000)]);

    /// <summary>
    /// The petal each README wheel shows highlighted: none on the convert wheel (its hub shows the photo's size, as a wheel
    /// does when it opens), 메타데이터 and 선반 on the others (their hubs name the petal).
    /// </summary>
    static (IReadOnlyList<WheelItem> Items, int? Highlight) ReadmeWheel(WheelMode mode)
    {
        var (items, steps) = mode switch
        {
            WheelMode.Convert => PhotoWheel(mode),
            WheelMode.Tools => PhotoWheel(mode, ZestTool.Metadata),
            _ => PhotoWheel(mode, DropAction.Shelf),
        };
        return (items, steps.Length == 0 ? null : steps[0]);
    }

    /// <summary>
    /// A README header: a rounded card (1 px line, transparent outside its corners) with the app mark over one soft citrus
    /// glow, "Zest" and the tagline, all centred.
    /// </summary>
    static FrameworkElement Header(string tagline, bool dark)
    {
        var surface = ZestColors.For(dark);
        var card = new Grid
        {
            Width = BannerWidth,
            Height = BannerHeight,
            Clip = new RectangleGeometry(new Rect(0, 0, BannerWidth, BannerHeight), 20, 20),
            Background = new SolidColorBrush(dark ? Color.FromRgb(0x16, 0x15, 0x11) : WheelStyle.ToColor(surface.Window)),
        };
        AsOnHighDpiScreen(card);
        AddLockup(card, tagline, dark, BannerHeight, mark: 144, title: 66, line: 22, glow: 250);
        card.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(WheelStyle.ToColor(surface.LineStrong)),
        });
        return card;
    }

    /// <summary>The social preview (1280 x 640): the English header's lockup a little larger, on an opaque page.</summary>
    static FrameworkElement Social()
    {
        var page = new Grid { Width = SocialWidth, Height = SocialHeight, Background = new SolidColorBrush(WheelStyle.ToColor(ZestColors.Light.Window)) };
        AsOnHighDpiScreen(page);
        AddLockup(page, TaglineEn, dark: false, SocialHeight, mark: 112, title: 52, line: 18, glow: 190);
        return page;
    }

    /// <summary>icon-1024.png: the light mark at 824 of 1024 px, centred so its shadow fits.</summary>
    static FrameworkElement Icon()
    {
        var mark = AppMark.Create(412, dark: false, shadowBlur: 0.1);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Margin = new Thickness(0, 0, 0, 10); // the shadow falls downward; lift the tile so the whole stays centred
        return new Grid { Width = 512, Height = 512, Children = { mark } };
    }

    /// <summary>
    /// Adds the lockup centred in <paramref name="height"/> (its optical centre a little above the middle, as the tagline
    /// is light) and the glow centred on its mark.
    /// </summary>
    static void AddLockup(Grid page, string tagline, bool dark, double height, double mark, double title, double line, double glow)
    {
        var lockup = Lockup(tagline, dark, mark, title, line);
        lockup.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var top = Math.Round((height - lockup.DesiredSize.Height) / 2 - height * 0.015);
        lockup.Margin = new Thickness(0, top, 0, 0);
        page.Children.Add(Glow(page.Width / 2, top + mark / 2, glow, dark));
        page.Children.Add(lockup);
    }

    /// <summary>The mark, "Zest" and the tagline stacked and centred.</summary>
    static StackPanel Lockup(string tagline, bool dark, double mark, double title, double line)
    {
        var surface = ZestColors.For(dark);
        var korean = tagline.Any(c => c is >= '\uAC00' and <= '\uD7A3');
        var appMark = AppMark.Create(mark, dark);
        appMark.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                appMark,
                new TextBlock
                {
                    Text = "Zest",
                    FontFamily = WheelStyle.FormatFont,
                    FontWeight = FontWeights.Bold,
                    FontSize = title,
                    Foreground = new SolidColorBrush(WheelStyle.ToColor(surface.Text)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, title * 0.22, 0, 0),
                },
                new TextBlock
                {
                    Text = tagline,
                    FontFamily = korean ? WheelStyle.KoreanFont : new FontFamily("Segoe UI Variable Text"),
                    FontWeight = korean ? WheelStyle.LabelWeight : FontWeights.Normal,
                    FontSize = line,
                    Foreground = new SolidColorBrush(WheelStyle.ToColor(surface.Text2)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, title * 0.06, 0, 0),
                },
            },
        };
    }

    /// <summary>
    /// One very soft glow centred on (<paramref name="x"/>, <paramref name="y"/>): lemon, tangerine and lime washes, each
    /// a radial fade to nothing, overlapping a little off centre so the three blend.
    /// </summary>
    static Canvas Glow(double x, double y, double radius, bool dark)
    {
        var canvas = new Canvas { IsHitTestVisible = false };
        var alpha = dark ? 0x11 : 0x1A;
        (Citrus Citrus, double Dx, double Dy)[] washes =
        [
            (ZestColors.Lemon, 0, -0.08),
            (ZestColors.Tangerine, 0.08, 0.05),
            (ZestColors.Lime, -0.08, 0.05),
        ];
        foreach (var (citrus, dx, dy) in washes)
        {
            var c = WheelStyle.ToColor(citrus.Fill);
            var r = radius;
            // (1 - t^2)^3: full at the centre, reaching zero with no slope at the rim, so there is no visible edge.
            var fade = new GradientStopCollection();
            for (var k = 0; k <= 16; k++)
            {
                var t = k / 16.0;
                fade.Add(new GradientStop(Color.FromArgb((byte)Math.Round(alpha * Math.Pow(1 - t * t, 3)), c.R, c.G, c.B), t));
            }
            var wash = new Ellipse
            {
                Width = 2 * r,
                Height = 2 * r,
                Fill = new RadialGradientBrush(fade),
            };
            Canvas.SetLeft(wash, x + dx * radius - r);
            Canvas.SetTop(wash, y + dy * radius - r);
            canvas.Children.Add(wash);
        }
        return canvas;
    }

    /// <summary>
    /// The three wheels side by side on a plain stage (<c>Zest.Sunken</c> on light, the window colour on dark), each with its
    /// README hub (the photo's size, or a highlighted petal's name) and its key name under it in a chip of the wheel's citrus.
    /// </summary>
    static FrameworkElement Wheels(bool dark)
    {
        var palette = WheelStyle.Palette(dark);
        var row = new UniformGrid { Rows = 1, Columns = Modes.Length, Margin = new Thickness(40, 0, 40, 0) };
        foreach (var mode in Modes)
        {
            var (items, highlight) = ReadmeWheel(mode);
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            column.Children.Add(Wheel(null, palette, mode, items, highlight, PhotoSummary));
            var chip = KeyChip(mode, dark, active: true, size: 16);
            chip.Margin = new Thickness(0, -14, 0, 0); // the wheel's canvas keeps 32 DIP of shadow room under the petals
            column.Children.Add(chip);
            row.Children.Add(column);
        }
        var stage = new Grid
        {
            Width = BannerWidth,
            Height = BannerHeight,
            Background = ZestTheme.Brush(dark ? "Zest.Window" : "Zest.Sunken"),
            Children = { row },
        };
        AsOnHighDpiScreen(stage);
        return stage;
    }

    /// <summary>
    /// The modifier key of <paramref name="mode"/> alone ("Shift", "Alt", "Ctrl"), so both READMEs can share it: active, in the
    /// wheel's ink on its wash; inactive, in the third text colour on nothing.
    /// </summary>
    static Border KeyChip(WheelMode mode, bool dark, bool active, double size)
    {
        var citrus = WheelStyle.Accent(mode);
        var surface = ZestColors.For(dark);
        return new Border
        {
            Background = active ? new SolidColorBrush(WheelStyle.ToColor(citrus.Wash(dark))) : Brushes.Transparent,
            CornerRadius = new CornerRadius(size * 2),
            Padding = new Thickness(size * 0.9, size * 0.3, size * 0.9, size * 0.38),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = KeyName(mode),
                FontFamily = WheelStyle.FormatFont,
                FontWeight = WheelStyle.FormatWeight,
                FontSize = size,
                Foreground = new SolidColorBrush(WheelStyle.ToColor(active ? citrus.Ink(dark) : surface.Text3)),
            },
        };
    }

    static string KeyName(WheelMode mode) => mode switch
    {
        WheelMode.Convert => "Shift",
        WheelMode.Tools => "Alt",
        _ => "Ctrl",
    };

    /// <summary>
    /// The demo: each wheel opens with the photo's size in the hub (held a second), then the highlight steps across its petals
    /// (PDF, JPG, WEBP; 압축, 메타데이터; 복사, 선반) at half a second a step, the hub naming each, and rests on the last; then
    /// the next wheel opens. The three key names under the wheel show which key is down. Frames go to a temporary folder and ffmpeg (from PATH) encodes them into a looping animated WebP.
    /// </summary>
    static void Demo(string folder, StringBuilder log)
    {
        var palette = WheelStyle.Palette(false);
        var scenes = new[]
        {
            (WheelMode.Convert, PhotoWheel(WheelMode.Convert, "pdf", "jpg", "webp")),
            (WheelMode.Tools, PhotoWheel(WheelMode.Tools, ZestTool.Compress, ZestTool.Metadata)),
            (WheelMode.Actions, PhotoWheel(WheelMode.Actions, DropAction.Copy, DropAction.Shelf)),
        };
        // Per scene: the open wheel (the hub showing the size) for two steps, each highlight for one, the last held for three more.
        var frames = new List<(WheelMode Mode, IReadOnlyList<WheelItem> Items, int? Highlight)>();
        foreach (var (mode, (items, steps)) in scenes)
        {
            frames.Add((mode, items, null));
            frames.Add((mode, items, null));
            foreach (var step in steps) frames.Add((mode, items, step));
            for (var i = 0; i < 3; i++) frames.Add((mode, items, steps[^1]));
        }

        var work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zest-demo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        for (var i = 0; i < frames.Count; i++)
        {
            var (mode, items, highlight) = frames[i];
            var frame = DemoFrame(palette, mode, items, highlight);
            Save(frame, work, $"frame_{i:D3}.png", log);
            if (i == 0) File.Copy(System.IO.Path.Combine(work, "frame_000.png"), System.IO.Path.Combine(folder, "demo-poster.png"), true);
        }

        var ffmpeg = Zest.Core.Convert.FfmpegLocator.Locate();
        if (ffmpeg is null)
        {
            log.AppendLine($"demo: ffmpeg not on PATH; {frames.Count} frames left in {work}");
            return;
        }
        var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, "demo.webp"));
        var start = new System.Diagnostics.ProcessStartInfo(ffmpeg)
        {
            WorkingDirectory = work,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
        {
            "-y", "-hide_banner", "-loglevel", "error", "-framerate", DemoFps.ToString(CultureInfo.InvariantCulture), "-i", "frame_%03d.png",
            "-c:v", "libwebp_anim", "-lossless", "0", "-q:v", "90", "-compression_level", "6", "-loop", "0", output,
        })
            start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg failed ({process.ExitCode}): {errors}");
        Directory.Delete(work, true);
        log.AppendLine(CultureInfo.InvariantCulture, $"demo.webp: {frames.Count} frames at {DemoFps} fps, {new FileInfo(output).Length} bytes");
    }

    /// <summary>One demo frame: the wheel on the flat <c>Zest.Sunken</c> stage, the three key names under it, the wheel's lit.</summary>
    static FrameworkElement DemoFrame(WheelPalette palette, WheelMode mode, IReadOnlyList<WheelItem> items, int? highlight)
    {
        var wheel = Wheel(null, palette, mode, items, highlight, PhotoSummary);
        wheel.HorizontalAlignment = HorizontalAlignment.Center;
        wheel.VerticalAlignment = VerticalAlignment.Top;
        wheel.Margin = new Thickness(0, -6, 0, 0);
        var keys = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 18),
        };
        foreach (var key in Modes)
        {
            var chip = KeyChip(key, palette.Dark, active: key == mode, size: 12);
            chip.Margin = new Thickness(4, 0, 4, 0);
            keys.Children.Add(chip);
        }
        var stage = new Grid
        {
            Width = DemoWidth,
            Height = DemoHeight,
            Background = new SolidColorBrush(WheelStyle.ToColor(ZestColors.Light.Sunken)),
            Children = { wheel, keys },
        };
        AsOnHighDpiScreen(stage);
        return stage;
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
        foreach (var popup in new[] { cards, Unstaged(ToolWindows.Compress(dark, SizeGoal.Percentage, wholeShadow: true)), Unstaged(ToolWindows.Shelf(dark, list: false)) })
        {
            popup.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(popup);
        }

        // As tall as the tallest popup (the compress window) plus even stage above and below.
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Grid
        {
            Width = PopupsWidth,
            Height = Math.Ceiling(row.DesiredSize.Height) + 2 * PopupsPad,
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

    /// <summary>Logs the typefaces the header's own text resolves to: the title's Latin face and both taglines'.</summary>
    static void LogHeaderFonts(StringBuilder log)
    {
        LogFont(log, "header-title", WheelStyle.FormatFont, FontWeights.Bold, 'Z');
        LogFont(log, "header-tagline-ko", WheelStyle.KoreanFont, WheelStyle.LabelWeight, '끌');
        LogFont(log, "header-tagline-en", new FontFamily("Segoe UI Variable Text"), FontWeights.Normal, 'H');
    }

    /// <summary>
    /// Petal counts with real catalog items, each drawn in the citrus of the wheel it comes from. 9 is a stress set (all tools
    /// plus three actions) since no single wheel has that many, so it is drawn as both the tools and the actions wheel.
    /// </summary>
    static IEnumerable<(int Count, WheelMode Mode, IReadOnlyList<WheelItem> Items, int Highlight, string Summary)> Sets()
    {
        var noOcr = Capabilities.All with { Ocr = false, BackgroundRemoval = false };
        var ocr = Capabilities.All with { BackgroundRemoval = false };
        // The plain renders show each hub summary shape: one file, several files, a lone folder, files with a folder.
        static HubEntry Sized(long bytes) => new(IsFolder: false, bytes);
        var folder = new HubEntry(IsFolder: true, null);
        var report = HubSummary.For([Sized(1_240_000)]); // 1.2 MB
        var photo = HubSummary.For([Sized(5_600_000)]); // 5.6 MB
        var three = HubSummary.For([Sized(5_600_000), Sized(4_300_000), Sized(2_500_000)]); // 3개 · 12.4 MB
        var lone = HubSummary.For([folder]); // 폴더 1개
        var mixed = HubSummary.For([Sized(5_600_000), Sized(4_300_000), folder]); // 파일 2개 · 폴더 1개, the longest
        yield return (2, WheelMode.Tools, WheelCatalog.Items(["report.pdf"], WheelMode.Tools, noOcr), 0, report); // split + metadata
        yield return (5, WheelMode.Tools, WheelCatalog.Items(["photo.png"], WheelMode.Tools, ocr), 2, three); // "PDF 만들기": the longest hub name
        yield return (5, WheelMode.Actions, WheelCatalog.Items(["photo.png"], WheelMode.Actions, Capabilities.All), 2, lone);
        yield return (7, WheelMode.Convert, WheelCatalog.Items(["photo.png"], WheelMode.Convert, Capabilities.All), 1, photo);
        var nine = WheelCatalog.Items(["photo.png"], WheelMode.Tools, Capabilities.All)
            .Concat(WheelCatalog.Items(["photo.png"], WheelMode.Actions, Capabilities.All).Take(3)).ToList();
        yield return (9, WheelMode.Tools, nine, 3, mixed);
        yield return (9, WheelMode.Actions, nine, 3, mixed);
    }

    static string ModeName(WheelMode mode) => mode switch
    {
        WheelMode.Convert => "convert",
        WheelMode.Tools => "tools",
        _ => "actions",
    };

    /// <summary>
    /// The wheel over <paramref name="backdrop"/> (review renders), or over nothing, so the stage it is placed on shows
    /// through (README images).
    /// </summary>
    static FrameworkElement Wheel(BitmapSource? backdrop, WheelPalette palette, WheelMode mode, IReadOnlyList<WheelItem> items, int? highlight, string summary)
    {
        var view = new WheelView { Animate = false, Palette = palette, Mode = mode };
        AsOnHighDpiScreen(view);
        if (items.Count == 0) view.ShowEmpty();
        else view.SetItems(items);
        view.Summary = summary;
        view.Highlight(highlight);

        var root = new Grid { Width = Side, Height = Side };
        if (backdrop is not null) root.Children.Add(new Image { Source = backdrop, Width = Side, Height = Side, Stretch = Stretch.Fill });
        root.Children.Add(view);
        return root;
    }

    /// <summary>
    /// Stand-in for the screen behind the wheel, rendered at 2x like a real capture on a 200 % display: a white file list
    /// half covered by a dark-green window. Review renders only; the README images use plain stages.
    /// </summary>
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

        // A dark-green window covering the left of the list.
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
