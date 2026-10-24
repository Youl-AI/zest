using System.Windows;
using System.Windows.Media;
using Zest.Core.Design;
using Microsoft.Win32;

namespace Zest.App.Theme;

/// <summary>
/// Publishes the Zest palette as frozen brushes in <c>Application.Current.Resources</c> and overrides the WPF-UI
/// keys Zest restyles. The keys live in the application dictionary itself, so they win over the
/// <c>ThemesDictionary</c> values that every <c>ApplicationThemeManager</c> call rewrites; call <see cref="Apply()"/>
/// right after each such call.
/// </summary>
public static class ZestTheme
{
    /// <summary>True when Windows apps use the dark theme (<c>AppsUseLightTheme</c> = 0; light when unset).</summary>
    public static bool IsDark =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int light && light == 0;

    /// <summary>
    /// The primary button's fill and text (tangerine; the same in both themes). WPF-UI paints every disabled button with
    /// <c>ButtonBackgroundDisabled</c>, so a primary that can be disabled carries these as its own <c>ButtonBackgroundDisabled</c>
    /// / <c>ButtonForegroundDisabled</c> resources and stays tangerine, faded by the footer style.
    /// </summary>
    public static readonly Color PrimaryFill = ToColor(ZestColors.Tangerine.Fill), PrimaryOnFill = ToColor(ZestColors.Tangerine.OnFill);

    static Color ToColor(Argb c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static void Apply() => Apply(IsDark);

    public static void Apply(bool dark)
    {
        var res = Application.Current.Resources;
        var s = ZestColors.For(dark);

        Set(res, "Zest.Window", s.Window);
        Set(res, "Zest.Raised", s.Raised);
        Set(res, "Zest.Sunken", s.Sunken);
        Set(res, "Zest.Line", s.Line);
        Set(res, "Zest.LineStrong", s.LineStrong);
        Set(res, "Zest.Seg", s.Seg);
        Set(res, "Zest.Text", s.Text);
        Set(res, "Zest.Text2", s.Text2);
        Set(res, "Zest.Text3", s.Text3);
        Set(res, "Zest.Danger", s.Danger);
        Set(res, "Zest.DangerWash", s.DangerWash);

        foreach (var c in new[] { ZestColors.Lemon, ZestColors.Tangerine, ZestColors.Lime })
        {
            Set(res, $"Zest.{c.Name}.Fill", c.Fill);
            Set(res, $"Zest.{c.Name}.OnFill", c.OnFill);
            Set(res, $"Zest.{c.Name}.Ink", c.Ink(dark));
            Set(res, $"Zest.{c.Name}.Wash", c.Wash(dark));
        }

        // WPF-UI overrides.
        var tangerine = ZestColors.Tangerine;
        Set(res, "ApplicationBackgroundBrush", s.Window);
        Set(res, "TextFillColorPrimaryBrush", s.Text);
        Set(res, "TextFillColorSecondaryBrush", s.Text2);
        Set(res, "TextFillColorTertiaryBrush", s.Text3);
        Set(res, "DividerStrokeColorDefaultBrush", s.Line);
        Set(res, "ControlStrokeColorDefaultBrush", s.LineStrong);
        Set(res, "CardBackgroundFillColorDefaultBrush", s.Sunken);
        Set(res, "CardStrokeColorDefaultBrush", s.Line);
        Set(res, "SubtleFillColorSecondaryBrush", s.Seg);
        Set(res, "SystemFillColorCriticalBrush", s.Danger);
        Set(res, "SystemFillColorSuccessBrush", ZestColors.Lime.Ink(dark));
        Set(res, "AccentFillColorDefaultBrush", tangerine.Fill);
        Set(res, "TextOnAccentFillColorPrimaryBrush", tangerine.OnFill);
        // WPF-UI's primary Button reads its own AccentButton* keys, which ignore the keys above; pin them to tangerine too.
        Set(res, "AccentButtonBackground", tangerine.Fill);
        Set(res, "AccentButtonBackgroundPointerOver", tangerine.Fill, 0.9);
        Set(res, "AccentButtonBackgroundPressed", tangerine.Fill, 0.8);
        Set(res, "AccentButtonBorderBrushPressed", tangerine.Fill, 0.8);
        Set(res, "AccentButtonForeground", tangerine.OnFill);
        Set(res, "AccentButtonForegroundPointerOver", tangerine.OnFill);
        Set(res, "AccentButtonForegroundPressed", tangerine.OnFill);
        // A disabled button keeps its normal colours; the footer button style fades it to 45 % (popup page .btn[disabled]).
        Set(res, "ButtonBackgroundDisabled", s.Seg);
        Set(res, "ButtonBorderBrushDisabled", Transparent);
        Set(res, "ButtonForegroundDisabled", s.Text);
        ApplyControlAccents(res, dark);
        res["ControlCornerRadius"] = new CornerRadius(8);
    }

    /// <summary>
    /// The WPF-UI 4.3.0 keys through which its CheckBox, RadioButton, Slider, ProgressBar, ComboBox, TextBox and ListBox
    /// templates show the system accent (each resolves <c>SystemAccentColorPrimary</c> or an <c>AccentFillColor*</c> key in
    /// its theme dictionary). Marks and tracks take the tangerine ink (the fill itself on dark) so they keep 3:1 on the
    /// window; the glyph on them is white on light, the dark on-fill text on dark. Fills meant as a wash (dropdown
    /// pill, text selection) take the fill.
    /// </summary>
    static void ApplyControlAccents(ResourceDictionary res, bool dark)
    {
        var t = ZestColors.Tangerine;
        var ink = t.Ink(dark);
        var onInk = dark ? t.OnFill : White;

        Set(res, "CheckBoxCheckBackgroundFillChecked", ink);
        Set(res, "CheckBoxCheckBackgroundFillCheckedPointerOver", ink, 0.9);
        Set(res, "CheckBoxCheckBackgroundFillCheckedPressed", ink, 0.8);
        Set(res, "CheckBoxCheckGlyphForeground", onInk);

        Set(res, "RadioButtonOuterEllipseCheckedStroke", ink);
        Set(res, "RadioButtonOuterEllipseCheckedStrokePointerOver", ink, 0.9);
        Set(res, "RadioButtonCheckGlyphFill", onInk);

        Set(res, "SliderThumbBackground", ink);
        Set(res, "SliderThumbBackgroundPointerOver", ink, 0.9);

        Set(res, "ProgressBarForeground", ink);
        Set(res, "TextControlFocusedBorderBrush", ink);
        Set(res, "ComboBoxBorderBrushFocused", ink);
        Set(res, "ComboBoxItemPillFillBrush", ink);
        Set(res, "ListBoxItemSelectedBackgroundThemeBrush", t.Fill);
        Set(res, "ListBoxItemSelectedForegroundThemeBrush", t.OnFill);
        Set(res, "AccentFillColorSelectedTextBackgroundBrush", t.Fill);
        Set(res, "AccentTextFillColorPrimaryBrush", ink);
        Set(res, "AccentTextFillColorSecondaryBrush", ink, 0.9);
        Set(res, "AccentTextFillColorTertiaryBrush", ink, 0.8);

        // Catch-all for any other WPF-UI brush built on the system accent colours (ApplicationAccentColorManager writes
        // these same keys here on every theme apply; this runs after it).
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorPrimary", "SystemAccentColorSecondary", "SystemAccentColorTertiary" })
        {
            res[key] = ToColor(ink);
            Set(res, key + "Brush", ink);
        }
    }

    static readonly Argb White = Argb.Hex("#FFFFFF");
    static readonly Argb Transparent = Argb.Hex("#00000000");

    /// <summary>The applied brush for a <c>Zest.*</c> key; throws when <see cref="Apply()"/> has not run.</summary>
    public static Brush Brush(string key) =>
        Application.Current.Resources[key] as Brush ?? throw new KeyNotFoundException($"No Zest brush '{key}'. Call ZestTheme.Apply() first.");

    static void Set(ResourceDictionary res, string key, Argb color, double opacity = 1)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B)) { Opacity = opacity };
        brush.Freeze();
        res[key] = brush;
    }
}
