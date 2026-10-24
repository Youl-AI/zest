using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Zest.Core.Catalog;
using Zest.Core.Design;
using Zest.App.Theme;

namespace Zest.App.Wheel;

/// <summary>Neutral colors of the wheel for one system theme; the accent comes from the mode's citrus (<see cref="WheelStyle.Accent"/>).</summary>
/// <param name="Petal">Petal fill.</param>
/// <param name="Label">Petal label and icon color.</param>
/// <param name="Hub">Round readout in the hole.</param>
/// <param name="HubText">Hovered label in the hub.</param>
/// <param name="ShadowColor">Drop shadow under the petals and the hub, so they stay distinct over a page of their own color.</param>
/// <param name="ShadowOpacity">Opacity of that shadow.</param>
public sealed record WheelPalette(
    bool Dark,
    Color Petal,
    Color Label,
    Color Hub,
    Color HubText,
    Color ShadowColor,
    double ShadowOpacity);

/// <summary>Every color, radius, stroke, font, size and motion value of the wheel, in one place.</summary>
public static class WheelStyle
{
    // ── Colors ──────────────────────────────────────────────────────────────
    public static readonly WheelPalette LightPalette = new(
        Dark: false,
        Petal: Hex(0xFF, 0xFF, 0xFF, 0xFF),
        Label: Hex(0xFF, 0x2A, 0x26, 0x22),
        Hub: Hex(0xFF, 0xFF, 0xFF, 0xFF),
        HubText: ToColor(ZestColors.Light.Text),
        ShadowColor: Hex(0xFF, 0x1D, 0x1B, 0x16),
        ShadowOpacity: 0.16);

    public static readonly WheelPalette DarkPalette = new(
        Dark: true,
        Petal: Hex(0xFF, 0x24, 0x22, 0x1C),
        Label: Hex(0xFF, 0xEC, 0xE7, 0xD8),
        Hub: Hex(0xFF, 0x1D, 0x1B, 0x16),
        HubText: ToColor(ZestColors.Dark.Text),
        ShadowColor: Hex(0xFF, 0x1D, 0x1B, 0x16),
        ShadowOpacity: 0.45);

    /// <summary>Palette for the current Windows app theme (<c>AppsUseLightTheme</c>; light when unset).</summary>
    public static WheelPalette SystemPalette() => Palette(ZestTheme.IsDark);

    public static WheelPalette Palette(bool dark) => dark ? DarkPalette : LightPalette;

    /// <summary>The mode's accent: Shift lemon, Alt tangerine, Ctrl lime.</summary>
    public static Citrus Accent(WheelMode mode) => ZestColors.For(mode);

    /// <summary>1 px line around every petal and the hub: the citrus fill at alpha 0x38 on dark, its ink at 0x59 on light.</summary>
    public static Color PetalEdge(Citrus citrus, bool dark) =>
        dark ? WithAlpha(citrus.Fill, 0x38) : WithAlpha(citrus.InkLight, 0x59);

    /// <summary>Text of the mode chip under the hub label.</summary>
    public static string ModeChip(WheelMode mode) => mode switch
    {
        WheelMode.Convert => "Shift 변환",
        WheelMode.Tools => "Alt 도구",
        WheelMode.Actions => "Ctrl 액션",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static Color ToColor(Argb c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>One shadow under the petals canvas and one under the hub (direction 270 = straight down).</summary>
    public static DropShadowEffect Shadow(WheelPalette palette)
    {
        var effect = new DropShadowEffect
        {
            Color = palette.ShadowColor,
            Opacity = palette.ShadowOpacity,
            BlurRadius = ShadowBlur,
            ShadowDepth = palette.Dark ? ShadowDepthDark : ShadowDepthLight,
            Direction = 270,
        };
        effect.Freeze();
        return effect;
    }

    public const double ShadowBlur = 18, ShadowDepthDark = 6, ShadowDepthLight = 4;

    // ── Geometry (canvas units = DIP; the wheel canvas is 320 x 320) ───────
    /// <summary>Round readout in the hole.</summary>
    public const double HubRadius = 40;
    public const double OuterCorner = 10, InnerCorner = 7;
    /// <summary>Label centres sit on this radius (original: (inner + outer) / 2 + 2).</summary>
    public const double LabelRadius = 93;
    /// <summary>How far the highlighted petal rises outward (original <c>liftDistance</c>).</summary>
    public const double Lift = 6;
    public const double PetalEdgeThickness = 1;
    /// <summary>Gap between a petal's icon and its label.</summary>
    public const double IconGap = 2;
    /// <summary>Horizontal room a label may use, as a fraction of the petal chord at <see cref="LabelRadius"/>.</summary>
    public const double LabelChordFraction = 0.8;
    public const double HubTextWidth = 72;
    /// <summary>Mode chip under the hub label: 10 px bold, padding 2 x 8, fully round.</summary>
    public static readonly Thickness ChipPadding = new(8, 2, 8, 2);
    public const double ChipSize = 10, ChipGap = 2;

    // ── Empty state ─────────────────────────────────────────────────────────
    public const string NothingToDo = "할 수 있는 작업 없음";
    public const double EmptyHeight = 40, EmptyPadding = 16, EmptyCorner = 10;

    // ── Type ────────────────────────────────────────────────────────────────
    public static readonly FontFamily FormatFont = new("Segoe UI Variable Display");
    public const double FormatSize = 15;
    public static readonly FontFamily KoreanFont = new(new Uri("pack://application:,,,/Zest;component/"), "./Assets/Fonts/#Pretendard");
    public const double KoreanSize = 12;
    /// <summary>Mode chip ("Shift 변환"): the modifier in Segoe UI Variable Display, the Hangul in Pretendard.</summary>
    public static readonly FontFamily ChipFont = new(new Uri("pack://application:,,,/Zest;component/"), "Segoe UI Variable Display, ./Assets/Fonts/#Pretendard");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons");
    public const double IconSize = 20;
    /// <summary>Hovered label in the hub: format names, Korean names.</summary>
    public const double HubFormatSize = 18, HubKoreanSize = 15;
    public const double EmptySize = 13;
    /// <summary>Korean text (the embedded Pretendard is SemiBold only).</summary>
    public static readonly FontWeight LabelWeight = FontWeights.SemiBold;
    /// <summary>Format names and the mode chip.</summary>
    public static readonly FontWeight FormatWeight = FontWeights.Bold;

    /// <summary>Segoe Fluent Icons glyph shown above a tool or action label (null for format petals, which are text only).</summary>
    public static string? IconFor(Operation operation) => operation switch
    {
        ToolOp { Tool: ZestTool.Compress } => "",         // BackToWindow: arrows pointing in
        ToolOp { Tool: ZestTool.Crop } => "",             // Crop
        ToolOp { Tool: ZestTool.ImagesToPdf } => "",      // PDF
        ToolOp { Tool: ZestTool.MergePdfs } => "",        // Merge
        ToolOp { Tool: ZestTool.SplitPdf } => "",         // Cut (scissors)
        ToolOp { Tool: ZestTool.CopyText } => "",         // Font (text)
        ToolOp { Tool: ZestTool.RemoveBackground } => "", // EraseTool
        ToolOp { Tool: ZestTool.Metadata } => "",   // Tag
        ActionOp { Action: DropAction.Shelf } => "",      // Package (tray box)
        ActionOp { Action: DropAction.Copy } => "",       // Copy
        ActionOp { Action: DropAction.Move } => "",       // MoveToFolder
        ActionOp { Action: DropAction.CopyPath } => "",   // Link
        ActionOp { Action: DropAction.Terminal } => "",   // CommandPrompt
        _ => null,
    };

    // ── Tool windows (compress, crop, order) ────────────────────────────────
    /// <summary>
    /// File names and numbers: the system UI face, with Hangul in a name from Malgun Gothic (regular weight, like the Latin
    /// beside it; the embedded Pretendard is SemiBold only and made "제주 바다.jpg" look half bold).
    /// </summary>
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Malgun Gothic");
    /// <summary>Crop selection outline and handles: the tools wheel's tangerine, so the editor reads as the same product.</summary>
    public static readonly Color CropAccent = ToColor(ZestColors.Tangerine.Fill);
    /// <summary>Dimming over the part of the image the crop will cut away.</summary>
    public static readonly Color CropShade = Hex(0x8C, 0x00, 0x00, 0x00);
    /// <summary>Hairline just outside the selection outline, so the accent stays visible on a bright image.</summary>
    public static readonly Color CropOutlineShadow = Hex(0x59, 0x00, 0x00, 0x00);
    public const double CropOutline = 1.5, CropHandleSize = 12, CropHandleHit = 20, CropHandleCorner = 3;

    // ── Motion (emil-design-eng values; nothing over 300 ms, no ease-in) ────
    /// <summary>CSS <c>ease</c>: color changes.</summary>
    public static readonly KeySpline Ease = Frozen(new KeySpline(0.25, 0.1, 0.25, 1));
    /// <summary><c>cubic-bezier(0.23, 1, 0.32, 1)</c>: strong ease-out for movement, fades and entrances.</summary>
    public static readonly KeySpline EaseOut = Frozen(new KeySpline(0.23, 1, 0.32, 1));
    public static readonly TimeSpan HighlightColor = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan HighlightLift = TimeSpan.FromMilliseconds(140);
    public static readonly TimeSpan CancelFade = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan CardEnter = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan CardExit = TimeSpan.FromMilliseconds(150);
    public const double CardRise = 8;

    static Color Hex(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    static Color WithAlpha(Argb c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    static KeySpline Frozen(KeySpline k)
    {
        k.Freeze();
        return k;
    }
}
