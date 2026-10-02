using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BijouHub.Converters;

/// <summary>
/// Maps a status tone ("success", "warning", "critical", "accent", "neutral") to the active
/// theme's brush. ConverterParameter="Soft" returns a faint wash of that color, for pill
/// backgrounds. Resolved at bind time, so the board is rebuilt when the theme changes.
/// </summary>
public class ToneBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "success" => "SuccessBrush",
            "warning" => "HazardBrush",
            "critical" => "DangerBrush",
            "accent" => "AccentBrush",
            _ => "MutedTextBrush"
        };
        var brush = Application.Current.TryFindResource(key) as SolidColorBrush ?? Brushes.Gray;
        if (!string.Equals(parameter as string, "Soft", StringComparison.OrdinalIgnoreCase)) return brush;

        var color = brush.Color;
        var soft = new SolidColorBrush(Color.FromArgb(0x2E, color.R, color.G, color.B));
        soft.Freeze();
        return soft;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
