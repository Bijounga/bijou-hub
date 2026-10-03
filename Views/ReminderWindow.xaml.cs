using System.Windows;

namespace BijouHub.Views;

// A goal's time has come: a small always-on-top card in the corner, seen even with another app
// in front. Doesn't steal focus.
public partial class ReminderWindow : Window
{
    public event Action? DoneRequested;
    public event Action? SnoozeRequested;

    public ReminderWindow(string task, string when)
    {
        InitializeComponent();
        TaskText.Text = task;
        WhenText.Text = when;
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Bottom - ActualHeight - 16;
        };
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        DoneRequested?.Invoke();
        Close();
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        SnoozeRequested?.Invoke();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
