using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BijouHub.Converters;

/// <summary>Null or empty string → Collapsed, anything else → Visible.</summary>
public class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
