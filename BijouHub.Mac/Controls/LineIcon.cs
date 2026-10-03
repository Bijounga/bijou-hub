using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace BijouHub.Mac.Controls;

/// <summary>
/// One consistent set of line icons (16×16 grid, round caps), drawn in the surrounding text
/// color so an icon in a button follows the button's state and the theme. Kind picks the icon;
/// Filled fills the shape instead (play, starred).
/// </summary>
public class LineIcon : Control
{
    private static readonly Dictionary<string, Geometry> Shapes = new()
    {
        ["add"] = Parse("M8 3v10M3 8h10"),
        ["edit"] = Parse("M10.6 2.6l2.8 2.8L5.9 12.9H3.1v-2.8zM9.2 4l2.8 2.8"),
        ["delete"] = Parse("M2.8 4.4h10.4M6.3 4.4V2.8h3.4v1.6M4.4 4.4l.7 9h5.8l.7-9M7 7v4M9 7v4"),
        ["refresh"] = Parse("M13 8a5 5 0 1 1-1.4-3.5M13 2.6v2.9h-2.9"),
        ["play"] = Parse("M5 3.2l8 4.8-8 4.8z"),
        ["pause"] = Parse("M5.5 3.5v9M10.5 3.5v9"),
        ["stop"] = Parse("M4 4h8v8H4z"),
        ["history"] = Parse("M2.6 8a5.4 5.4 0 1 0 1.6-3.8M2.6 2.6v2.9h2.9M8 5.2V8l2 1.4"),
        ["folder"] = Parse("M2 4.6h4.2l1.4 1.4H14v7.4H2z"),
        ["power"] = Parse("M8 2.2v5.6M4.7 4.4a5 5 0 1 0 6.6 0"),
        ["pin"] = Parse("M5.5 2.5h5M7 2.5v3.6L4.6 9h6.8L9 6.1V2.5M8 9v4.8"),
        ["palette"] = Parse("M8 2.2a5.8 5.8 0 1 0 0 11.6c1.2 0 1.4-1 .9-1.8-.5-.9.1-1.8 1.1-1.8h1.8a2 2 0 0 0 2-2A6 6 0 0 0 8 2.2zM5 7.3h.1M7.2 4.9h.1M10.2 5.4h.1"),
        ["deck"] = Parse("M3 3h4v4H3zM9 3h4v4H9zM3 9h4v4H3zM9 9h4v4H9z"),
        ["note"] = Parse("M3 2.6h10v7.6l-3.2 3.2H3zM9.8 13.4v-3.2H13"),
        ["popout"] = Parse("M9.2 2.6h4.2v4.2M13.4 2.6L8 8M11.2 9.6v3.8H2.6V4.8h3.8"),
        ["dock"] = Parse("M2.6 13.4L7 9M3.4 9H7v3.6M13.4 2.6L9 7M12.6 7H9V3.4"),
        ["clock"] = Parse("M8 2.5a5.5 5.5 0 1 1 0 11a5.5 5.5 0 1 1 0-11zM8 5.2V8l2.1 1.3"),
        ["cloud"] = Parse("M4.6 12.4h7.3a2.9 2.9 0 0 0 .3-5.8A4.4 4.4 0 0 0 3.7 7.9a2.3 2.3 0 0 0 .9 4.5z"),
        ["sync"] = Parse("M3 7a5 5 0 0 1 9-2.2M13 9a5 5 0 0 1-9 2.2M12.4 2.4v2.6H9.8M3.6 13.6V11h2.6"),
        ["warning"] = Parse("M8 2.2l6.4 11.2H1.6zM8 6.4v3.2M8 11.6v.1"),
        ["check"] = Parse("M3.2 8.4l3 3 6.6-6.8"),
        ["close"] = Parse("M4.2 4.2l7.6 7.6M11.8 4.2l-7.6 7.6"),
        ["star"] = Parse("M8 2.2l1.8 3.7 4 .6-2.9 2.8.7 4L8 11.4l-3.6 1.9.7-4L2.2 6.5l4-.6z"),
        ["grip"] = Parse("M6 4h.1M10 4h.1M6 8h.1M10 8h.1M6 12h.1M10 12h.1"),
        ["link"] = Parse("M6.8 9.2a2.6 2.6 0 0 0 3.7 0l2-2a2.6 2.6 0 0 0-3.7-3.7l-.8.8M9.2 6.8a2.6 2.6 0 0 0-3.7 0l-2 2a2.6 2.6 0 0 0 3.7 3.7l.8-.8"),
        ["app"] = Parse("M3 3h10v10H3zM3 6h10"),
        ["chevron"] = Parse("M6 3.5L10.5 8 6 12.5"),
        ["download"] = Parse("M8 2.5v8M4.8 7.6L8 10.8l3.2-3.2M3 13.4h10"),
        ["google"] = Parse("M4.6 12.4h7.3a2.9 2.9 0 0 0 .3-5.8A4.4 4.4 0 0 0 3.7 7.9a2.3 2.3 0 0 0 .9 4.5z"),
    };

    // The surrounding text color by default (inherited), or set it directly.
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<LineIcon>();

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<LineIcon, string>(nameof(Kind), "add");

    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<LineIcon, bool>(nameof(Filled));

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<LineIcon, double>(nameof(StrokeWidth), 1.5);

    static LineIcon()
    {
        AffectsRender<LineIcon>(KindProperty, FilledProperty, StrokeWidthProperty, ForegroundProperty);
        WidthProperty.OverrideDefaultValue<LineIcon>(16);
        HeightProperty.OverrideDefaultValue<LineIcon>(16);
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public bool Filled
    {
        get => GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (!Shapes.TryGetValue(Kind ?? "", out var shape)) return;

        var brush = Foreground ?? Brushes.White;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 16.0;
        var offset = new Point((Bounds.Width - 16 * scale) / 2, (Bounds.Height - 16 * scale) / 2);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            // StrokeWidth is in screen pixels; the drawing is scaled, so divide it back out.
            var pen = new Pen(brush, StrokeWidth / Math.Max(scale, 0.01), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(Filled ? brush : null, pen, shape);
        }
    }

    private static Geometry Parse(string data) => StreamGeometry.Parse(data);
}
