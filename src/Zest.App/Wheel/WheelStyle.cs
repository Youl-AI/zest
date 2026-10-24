using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Zest.Core.Catalog;
using Zest.App.Theme;

namespace Zest.App.Wheel;

/// <summary>Colors of the glass wheel for one system theme.</summary>
/// <param name="Tint">Film over the blurred screen capture inside the disc.</param>
/// <param name="TintOpaque">Disc fill when no capture is available.</param>
/// <param name="Petal">Petal fill, one step lighter than the tint.</param>
/// <param name="PetalEdge">Hairline around each petal so petals stay distinct over a plain white or black screen.</param>
/// <param name="Hub">Round readout in the hole.</param>
/// <param name="Label">Petal and hub text.</param>
/// <param name="Highlight">Lime fill of the highlighted petal.</param>
/// <param name="HighlightTop">Top of the highlight gradient (the original's lime at 82 %).</param>
/// <param name="HighlightStroke">Hairline around the highlighted petal.</param>
/// <param name="InnerHighlight">0.5 px light line just inside the disc edge.</param>
/// <param name="Ring">1 px lime ring on the disc edge.</param>
public sealed record WheelPalette(
    bool Dark,
    Color Tint,
    Color TintOpaque,
    Color Petal,
    Color PetalEdge,
    Color Hub,
    Color Label,
    Color Highlight,
    Color HighlightTop,
    Color HighlightStroke,
    Color InnerHighlight,
    Color Ring);

/// <summary>Every color, radius, stroke, font, size and motion value of the wheel, in one place.</summary>
public static class WheelStyle
{
    // ── Colors ──────────────────────────────────────────────────────────────
    public static readonly WheelPalette LightPalette = new(
        Dark: false,
        Tint: Hex(0xB3, 0xF2, 0xF2, 0xF2),
        TintOpaque: Hex(0xFF, 0xF2, 0xF2, 0xF2),
        Petal: Hex(0xCC, 0xFF, 0xFF, 0xFF),
        PetalEdge: Hex(0x14, 0x00, 0x00, 0x00),
        Hub: Hex(0xB3, 0xFF, 0xFF, 0xFF),
        Label: Hex(0xFF, 0x1F, 0x1F, 0x1F),
        Highlight: Hex(0xFF, 0x8B, 0xC3, 0x4A),
        HighlightTop: Hex(0xD1, 0x8B, 0xC3, 0x4A),
        HighlightStroke: Hex(0x80, 0xFF, 0xFF, 0xFF),
        InnerHighlight: Hex(0x40, 0xFF, 0xFF, 0xFF),
        Ring: Hex(0x59, 0x8B, 0xC3, 0x4A));

    public static readonly WheelPalette DarkPalette = new(
        Dark: true,
        // Brief: tint #B31C1C1C, petal #CC3A3A3A. Over a bright page those made petals darker than the disc (#41 vs #5D);
        // a denser film and a lighter petal keep "petal one step lighter" on any backdrop (see docs/decisions.md).
        Tint: Hex(0xD9, 0x1C, 0x1C, 0x1C),
        TintOpaque: Hex(0xFF, 0x1C, 0x1C, 0x1C),
        Petal: Hex(0xCC, 0x48, 0x48, 0x48),
        PetalEdge: Hex(0x14, 0xFF, 0xFF, 0xFF),
        Hub: Hex(0x14, 0xFF, 0xFF, 0xFF), // a step above the disc, like the original dark wheel's #2E hub
        Label: Hex(0xFF, 0xFF, 0xFF, 0xFF),
        Highlight: Hex(0xFF, 0x6F, 0xA8, 0x3A),
        HighlightTop: Hex(0xD1, 0x6F, 0xA8, 0x3A),
        HighlightStroke: Hex(0x80, 0xFF, 0xFF, 0xFF),
        InnerHighlight: Hex(0x40, 0xFF, 0xFF, 0xFF),
        Ring: Hex(0x59, 0x6F, 0xA8, 0x3A));

    public static readonly Color HighlightText = Colors.White;

    /// <summary>Palette for the current Windows app theme (<c>AppsUseLightTheme</c>; light when unset).</summary>
    public static WheelPalette SystemPalette()
        => ZestTheme.IsDark ? DarkPalette : LightPalette;

    // ── Geometry (canvas units = DIP; the wheel canvas is 320 x 320) ───────
    /// <summary>Tinted disc behind the petals (original: outer + 8).</summary>
    public const double DiscRadius = 136;
    /// <summary>Round readout in the hole (original: hole * 2 - 4 wide).</summary>
    public const double HubRadius = 44;
    public const double OuterCorner = 9, InnerCorner = 5;
    /// <summary>Label centres sit on this radius (original: (inner + outer) / 2 + 2).</summary>
    public const double LabelRadius = 93;
    /// <summary>How far the highlighted petal rises outward (original <c>liftDistance</c>).</summary>
    public const double Lift = 5;
    public const double RingThickness = 1, InnerHighlightThickness = 0.5, HighlightStrokeThickness = 1, PetalEdgeThickness = 1;
    /// <summary>Gap between a petal's icon and its label.</summary>
    public const double IconGap = 2;
    /// <summary>Horizontal room a label may use, as a fraction of the petal chord at <see cref="LabelRadius"/>.</summary>
    public const double LabelChordFraction = 0.8;
    public const double HubTextWidth = 76;

    // ── Glass ───────────────────────────────────────────────────────────────
    public const double BlurRadius = 24;

    // ── Empty state ─────────────────────────────────────────────────────────
    public const string NothingToDo = "할 수 있는 작업 없음";
    public const double EmptyHeight = 40, EmptyPadding = 16, EmptyCorner = 10;

    // ── Type ────────────────────────────────────────────────────────────────
    public static readonly FontFamily FormatFont = new("Segoe UI Variable Display");
    public const double FormatSize = 15;
    public static readonly FontFamily KoreanFont = new(new Uri("pack://application:,,,/Zest;component/"), "./Assets/Fonts/#Pretendard");
    public const double KoreanSize = 12;
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons");
    public const double IconSize = 20;
    public const double HubSize = 13;
    public const double EmptySize = 13;
    public static readonly FontWeight LabelWeight = FontWeights.SemiBold;

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
    /// <summary>Crop selection outline and handles use the wheel's highlight lime, so the editor reads as the same product.</summary>
    public static Color CropAccent(WheelPalette palette) => palette.Highlight;
    /// <summary>Dimming over the part of the image the crop will cut away.</summary>
    public static readonly Color CropShade = Hex(0x8C, 0x00, 0x00, 0x00);
    /// <summary>Hairline just outside the selection outline, so lime stays visible on a bright image.</summary>
    public static readonly Color CropOutlineShadow = Hex(0x59, 0x00, 0x00, 0x00);
    public const double CropOutline = 1.5, CropHandleSize = 10, CropHandleHit = 20, CropHandleCorner = 2;

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

    static KeySpline Frozen(KeySpline k)
    {
        k.Freeze();
        return k;
    }
}
