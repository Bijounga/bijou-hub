using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BijouHub.Mac.Controls;

// "#RRGGBB" (a channel's color) → a brush; nothing for null or an unreadable value.
public sealed class HexBrushConverter : IValueConverter
{
    public static readonly HexBrushConverter Instance = new();

    public static IBrush? Parse(string? hex) =>
        hex != null && Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : null;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Parse(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
