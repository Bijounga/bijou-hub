using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// The countdown ran out: a small always-on-top card in the screen's corner, seen even with
// another app in front. Doesn't steal focus.
public partial class TimeUpWindow : Window
{
    public event Action? Extended;
    public event Action? FinishRequested;

    public TimeUpWindow() : this("the session", 0) { }

    public TimeUpWindow(string title, int targetMinutes)
    {
        InitializeComponent();
        MessageText.Text = $"{DurationText.Format(Math.Max(1, targetMinutes))} on {title}. Keep going or wrap it up.";
        Opened += (_, _) =>
        {
            if (Screens.Primary is { } screen)
            {
                var area = screen.WorkingArea;
                var scale = screen.Scaling;
                Position = new PixelPoint(area.Right - (int)((Width + 16) * scale), area.Bottom - (int)((Height + 16) * scale));
            }
        };
    }

    private void Drag_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button) BeginMoveDrag(e);
    }

    private void Extend_Click(object? sender, RoutedEventArgs e)
    {
        Extended?.Invoke();
        Close();
    }

    private void Finish_Click(object? sender, RoutedEventArgs e)
    {
        FinishRequested?.Invoke();
        Close();
    }
}
