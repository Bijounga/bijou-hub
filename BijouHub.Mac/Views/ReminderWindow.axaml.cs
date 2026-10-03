using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BijouHub.Mac.Views;

// A goal's time has come: a small always-on-top card in the corner, seen even with another app
// in front. Doesn't steal focus.
public partial class ReminderWindow : Window
{
    public event Action? DoneRequested;
    public event Action? SnoozeRequested;

    public ReminderWindow() : this("", "") { }

    public ReminderWindow(string task, string when)
    {
        InitializeComponent();
        TaskText.Text = task;
        WhenText.Text = when;
    }

    private void Done_Click(object? sender, RoutedEventArgs e)
    {
        DoneRequested?.Invoke();
        Close();
    }

    private void Snooze_Click(object? sender, RoutedEventArgs e)
    {
        SnoozeRequested?.Invoke();
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
