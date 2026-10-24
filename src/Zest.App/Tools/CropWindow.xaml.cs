using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Zest.App.Theme;
using Zest.Core.Tools;

namespace Zest.App.Tools;

/// <summary>
/// Crop editor: the selection on the preview and the X/Y/폭/높이 fields (original pixels) edit the same rect; a ratio chip
/// constrains both and snaps the selection at once. 자르기 (or Enter) hands the rect to the caller.
/// </summary>
public partial class CropWindow : ZestWindow
{
    /// <summary>Largest initial preview in DIP; the caller decodes the preview for this size at the monitor's scale.</summary>
    public const double PreviewMaxLongEdge = 736;
    const double PreviewMinHeight = 240, PreviewMaxHeight = 520;
    /// <summary>Preview area chrome around the image: the canvas inset, 16 each side.</summary>
    const double PreviewChrome = 2 * 16;
    /// <summary>Window width outside the preview area: the shadow margins, the frame's 1 px edges and the preview's 16 px insets.</summary>
    const double PreviewOutside = 2 * ShadowMargin + 2 + 2 * 16;

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
    readonly RadioButton[] _chips;
    int _ratio;

    /// <param name="preview">Orientation-applied, downscaled preview.</param>
    /// <param name="sourceWidth">Original width in pixels (orientation applied), the unit of the fields and of the result.</param>
    public CropWindow(string source, BitmapSource preview, int sourceWidth, int sourceHeight, Action<PixelRect> crop)
    {
        _w = sourceWidth;
        _h = sourceHeight;
        _crop = crop;
        InitializeComponent();

        Heading = $"자르기 · {Path.GetFileName(source)}";
        Canvas.SetImage(preview, sourceWidth, sourceHeight);

        // Initial preview height follows the image's aspect at the window's width; released after the first frame so the window can be resized.
        var innerWidth = Width - PreviewOutside - PreviewChrome;
        PreviewArea.Height = Math.Clamp(innerWidth * sourceHeight / Math.Max(1, sourceWidth) + PreviewChrome, PreviewMinHeight, PreviewMaxHeight);
        ContentRendered += (_, _) =>
        {
            SizeToContent = SizeToContent.Manual;
            PreviewArea.Height = double.NaN;
        };

        // One chip per Ratios entry; chip i is ratio i (the indices the render harness and the old combo used).
        var chipStyle = (Style)FindResource("Zest.ChoiceChip");
        _chips = new RadioButton[Ratios.Length];
        for (var i = 0; i < Ratios.Length; i++)
        {
            var index = i;
            var chip = new RadioButton
            {
                Content = Ratios[i].Label,
                GroupName = "Ratio",
                IsChecked = i == 0,
                Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                Style = chipStyle,
            };
            chip.Checked += (_, _) =>
            {
                _ratio = index;
                ApplyAspect();
            };
            _chips[i] = chip;
            RatioPanel.Children.Add(chip);
        }

        Canvas.SelectionChanged += _ => ShowNumbers();
        Bind(XBox, CropField.X);
        Bind(YBox, CropField.Y);
        Bind(WidthBox, CropField.Width);
        Bind(HeightBox, CropField.Height);
        CropButton.Click += (_, _) => Crop();
        ShowNumbers();
    }

    double? Aspect => Ratios[_ratio].Aspect;

    /// <summary>Render harness: a ratio chip and a selection drawn with the tangerine accent.</summary>
    internal void Preview(int ratioIndex, PixelRect selection)
    {
        _chips[ratioIndex].IsChecked = true;
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
