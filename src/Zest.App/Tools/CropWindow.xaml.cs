using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Zest.App.Wheel;
using Zest.Core.Tools;
using Wpf.Ui.Controls;

namespace Zest.App.Tools;

/// <summary>
/// Crop editor: the selection on the preview and the X/Y/폭/높이 fields (original pixels) edit the same rect; an aspect
/// preset constrains both and snaps the selection at once. 자르기 (or Enter) hands the rect to the caller.
/// </summary>
public partial class CropWindow : FluentWindow
{
    /// <summary>Largest initial preview in DIP; the caller decodes the preview for this size at the monitor's scale.</summary>
    public const double PreviewMaxLongEdge = 736;
    const double PreviewMinHeight = 240, PreviewMaxHeight = 520;
    /// <summary>Preview area chrome around the image: canvas inset (16 each side) + 1 px border each side.</summary>
    const double PreviewChrome = 2 * 16 + 2;

    static readonly (string Label, double? Aspect)[] Ratios =
    [
        ("자유", null),
        ("1:1", 1),
        ("4:3", 4 / 3.0),
        ("3:2", 3 / 2.0),
        ("16:9", 16 / 9.0),
        ("9:16", 9 / 16.0),
    ];

    readonly int _w, _h;
    readonly Action<PixelRect> _crop;

    /// <param name="preview">Orientation-applied, downscaled preview.</param>
    /// <param name="sourceWidth">Original width in pixels (orientation applied), the unit of the fields and of the result.</param>
    public CropWindow(string source, BitmapSource preview, int sourceWidth, int sourceHeight, Action<PixelRect> crop)
    {
        _w = sourceWidth;
        _h = sourceHeight;
        _crop = crop;
        InitializeComponent();

        var title = $"{Path.GetFileName(source)} 자르기";
        Title = title;
        Bar.Title = title;
        Canvas.SetAccent(WheelStyle.CropAccent(WheelStyle.SystemPalette()));
        Canvas.SetImage(preview, sourceWidth, sourceHeight);

        // Initial preview height follows the image's aspect at the window's width; released after the first frame so the window can be resized.
        var innerWidth = Width - 32 - PreviewChrome;
        PreviewArea.Height = Math.Clamp(innerWidth * sourceHeight / Math.Max(1, sourceWidth) + PreviewChrome, PreviewMinHeight, PreviewMaxHeight);
        ContentRendered += (_, _) =>
        {
            SizeToContent = SizeToContent.Manual;
            PreviewArea.Height = double.NaN;
        };

        foreach (var (label, _) in Ratios) AspectBox.Items.Add(new ComboBoxItem { Content = label, FontFamily = label == "자유" ? WheelStyle.KoreanFont : WheelStyle.TextFont });
        AspectBox.SelectedIndex = 0;
        AspectBox.SelectionChanged += (_, _) => ApplyAspect();

        Canvas.SelectionChanged += _ => ShowNumbers();
        Bind(XBox, CropField.X);
        Bind(YBox, CropField.Y);
        Bind(WidthBox, CropField.Width);
        Bind(HeightBox, CropField.Height);
        CropButton.Click += (_, _) => Crop();
        ShowNumbers();
    }

    double? Aspect => Ratios[Math.Max(0, AspectBox.SelectedIndex)].Aspect;

    /// <summary>Render harness: the theme's accent, a preset and a selection drawn with it.</summary>
    internal void Preview(WheelPalette palette, int ratioIndex, PixelRect selection)
    {
        Canvas.SetAccent(WheelStyle.CropAccent(palette));
        AspectBox.SelectedIndex = ratioIndex;
        Canvas.Selection = CropMath.FitAspect(selection, Aspect, _w, _h);
        ShowNumbers();
    }

    void ApplyAspect()
    {
        Canvas.Aspect = Aspect;
        Canvas.Selection = CropMath.FitAspect(Canvas.Selection, Aspect, _w, _h); // snaps, no transition
        ShowNumbers();
    }

    void Bind(System.Windows.Controls.TextBox box, CropField field)
    {
        box.LostKeyboardFocus += (_, _) => Commit(box, field);
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
    }

    /// <summary>Applies a typed value; anything that is not a whole number is put back to the current value.</summary>
    void Commit(System.Windows.Controls.TextBox box, CropField field)
    {
        var current = Value(Canvas.Selection, field);
        if (int.TryParse(box.Text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) && v != current)
            Canvas.Selection = CropMath.FromField(Canvas.Selection, field, v, Aspect, _w, _h);
        ShowNumbers();
    }

    static int Value(PixelRect r, CropField f) => f switch
    {
        CropField.X => r.X,
        CropField.Y => r.Y,
        CropField.Width => r.Width,
        _ => r.Height,
    };

    void ShowNumbers()
    {
        var r = Canvas.Selection;
        XBox.Text = r.X.ToString(CultureInfo.InvariantCulture);
        YBox.Text = r.Y.ToString(CultureInfo.InvariantCulture);
        WidthBox.Text = r.Width.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = r.Height.ToString(CultureInfo.InvariantCulture);
    }

    void Crop()
    {
        // Enter in a field presses 자르기 without moving focus, so apply what was typed there first
        // (the other fields were applied when they lost focus).
        foreach (var (box, field) in new[] { (XBox, CropField.X), (YBox, CropField.Y), (WidthBox, CropField.Width), (HeightBox, CropField.Height) })
            if (box.IsKeyboardFocusWithin) Commit(box, field);
        _crop(Canvas.Selection);
        Close();
    }
}
