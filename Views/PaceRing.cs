using System.Windows;
using System.Windows.Media;

namespace BijouHub.Views;

// A small circle filling clockwise from the top: how far along a task's pace is. Green once it's full.
public class PaceRing : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(PaceRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var thickness = Math.Max(2, size / 7);
        var radius = (size - thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(TryFindResource("BorderBrush") as Brush ?? Brushes.Gray, thickness), center, radius, radius);

        var fraction = Math.Clamp(Fraction, 0, 1);
        if (fraction <= 0) return;

        var pen = new Pen(TryFindResource(fraction >= 1 ? "SuccessBrush" : "AccentBrush") as Brush ?? Brushes.Cyan, thickness)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
        };
        if (fraction >= 0.9999)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = fraction * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(center.X, center.Y - radius) };
        figure.Segments.Add(new ArcSegment(
            new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)),
            new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        dc.DrawGeometry(null, pen, new PathGeometry(new[] { figure }));
    }
}
