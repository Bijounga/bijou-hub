using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BijouHub.Converters;

/// <summary>
/// Count == 0 → Visible (or the reverse with ConverterParameter="NonZero"). Used instead of a
/// local Style with DataTriggers, which would replace the theme's implicit style for the control.
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isZero = value is int count && count == 0;
        var showWhenZero = !string.Equals(parameter as string, "NonZero", StringComparison.OrdinalIgnoreCase);
        return isZero == showWhenZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
