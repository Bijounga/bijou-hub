using System.Windows;
using System.Windows.Input;

namespace BijouHub.Views;

public partial class TimerPopoutWindow : Window
{
    public event Action? DockRequested;
    public event Action? FinishRequested;

    public TimerPopoutWindow()
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 16;
    }

    public void UpdateDisplay(string name, string timerText, string statusText)
    {
        NameText.Text = name;
        TimerText.Text = timerText;
        StatusText.Text = statusText;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, MouseButtonEventArgs e) => DockRequested?.Invoke();

    private void Dock_Click(object sender, RoutedEventArgs e) => DockRequested?.Invoke();

    private void Finish_Click(object sender, RoutedEventArgs e) => FinishRequested?.Invoke();
}
