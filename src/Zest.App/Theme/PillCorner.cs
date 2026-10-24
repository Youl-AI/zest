using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Zest.App.Theme;

/// <summary>
/// Height → <see cref="CornerRadius"/> of half that height: a fully round pill (CSS <c>border-radius: 999px</c>). WPF does
/// not clamp an oversized radius the way CSS does; it scales the radii into ellipses, so the radius must be exact.
/// </summary>
public sealed class PillCorner : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new CornerRadius(value is double h && double.IsFinite(h) ? h / 2 : 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
