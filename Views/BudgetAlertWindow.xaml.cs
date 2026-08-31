using System.Windows;

namespace BijouHub.Views;

public partial class BudgetAlertWindow : Window
{
    public event Action? Extended;
    public event Action? FinishRequested;

    public BudgetAlertWindow(string projectName, int targetMinutes)
    {
        InitializeComponent();
        MessageText.Text = $"You've hit your {targetMinutes}-minute budget for \"{projectName}\".";
    }

    private void Extend_Click(object sender, RoutedEventArgs e) => Extended?.Invoke();

    private void FinishNow_Click(object sender, RoutedEventArgs e) => FinishRequested?.Invoke();
}
