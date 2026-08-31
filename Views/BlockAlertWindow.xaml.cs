using System.Windows;

namespace BijouHub.Views;

public partial class BlockAlertWindow : Window
{
    public event Action? Allowed;
    public event Action? Dismissed;

    public BlockAlertWindow(string processName)
    {
        InitializeComponent();
        MessageText.Text = $"{processName} was opened — blocked in this mode.";
    }

    private void Allow_Click(object sender, RoutedEventArgs e) => Allowed?.Invoke();

    private void CloseIt_Click(object sender, RoutedEventArgs e) => Dismissed?.Invoke();
}
