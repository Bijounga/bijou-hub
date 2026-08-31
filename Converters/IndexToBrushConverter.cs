using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BijouHub.Converters;

/// <summary>
/// Cycles a fixed jewel-tone palette by index, used to give each mode card in the
/// sidebar a distinct accent stripe so the list reads as more than a plain text list.
/// </summary>
public class IndexToBrushConverter : IValueConverter
{
    private static readonly Brush[] Palette =
    {
        new SolidColorBrush(Color.FromRgb(0x35, 0xC1, 0xF0)), // brand blue
        new SolidColorBrush(Color.FromRgb(0xE0, 0x38, 0x4D)), // brand red
        new SolidColorBrush(Color.FromRgb(0xD7, 0xE6, 0xF0)), // brand white/silver
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var index = value is int i ? i : 0;
        if (index < 0) index = 0;
        return Palette[index % Palette.Length];
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
