using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Zest.App.Theme;

/// <summary>
/// The one soft shadow of the progress cards and the shelf (blur 20, 4 px down; 10 % on light, 40 % on dark). It sits on
/// its own layer under the surface, so the effect never rasterizes the text, and follows the <c>Zest.Window</c> brush,
/// so a live light/dark switch re-picks the strength.
/// </summary>
internal static class CardShadow
{
    const double LightOpacity = 0.10, DarkOpacity = 0.40;

    static readonly DependencyProperty SurfaceProperty = DependencyProperty.RegisterAttached(
        "Surface", typeof(Brush), typeof(CardShadow), new PropertyMetadata(null, OnSurfaceChanged));

    /// <summary>Gives <paramref name="layer"/> (a border of the surface's shape, under it) the shadow.</summary>
    public static void Attach(Border layer) => layer.SetResourceReference(SurfaceProperty, "Zest.Window");

    static void OnSurfaceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var dark = e.NewValue is SolidColorBrush { Color: var c } && 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B < 128;
        ((UIElement)d).Effect = new DropShadowEffect
        {
            BlurRadius = 20,
            ShadowDepth = 4,
            Direction = 270,
            Color = Colors.Black,
            Opacity = dark ? DarkOpacity : LightOpacity,
        };
    }
}
