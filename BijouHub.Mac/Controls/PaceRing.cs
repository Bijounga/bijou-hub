using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BijouHub.Mac.Controls;

// A small circle filling clockwise from the top: how far along a task's pace is. Green once it's full.
public class PaceRing : Control
{
    public static readonly StyledProperty<double> FractionProperty = AvaloniaProperty.Register<PaceRing, double>(nameof(Fraction));

    static PaceRing() => AffectsRender<PaceRing>(FractionProperty);

    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    private IBrush Find(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var thickness = Math.Max(2, size / 7);
        var radius = (size - thickness) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(null, new Pen(Find("BorderBrush", Brushes.Gray), thickness), center, radius, radius);

        var fraction = Math.Clamp(Fraction, 0, 1);
        if (fraction <= 0) return;

        var pen = new Pen(Find(fraction >= 1 ? "SuccessBrush" : "AccentBrush", Brushes.Cyan), thickness, lineCap: PenLineCap.Round);
        if (fraction >= 0.9999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = fraction * 2 * Math.PI;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(center.X, center.Y - radius), false);
            ctx.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)), new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
