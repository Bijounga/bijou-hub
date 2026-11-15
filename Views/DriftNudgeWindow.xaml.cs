using System.Windows;
using System.Windows.Threading;

namespace BijouHub.Views;

// A small card in the corner, never grabbing the keyboard: "still on it?". It goes away by itself.
public partial class DriftNudgeWindow : Window
{
    public event Action? BackToWork;
    public event Action? Snoozed;
    public event Action? TakeBreak;
    public event Action? NotDistraction;

    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(90) };

    public DriftNudgeWindow(string workingOn, string what)
    {
        InitializeComponent();
        TitleText.Text = $"Still on {workingOn}?";
        BodyText.Text = $"You've been in {what} for a bit.";
        _autoClose.Tick += (_, _) => { _autoClose.Stop(); Close(); };
        _autoClose.Start();
        Closed += (_, _) => _autoClose.Stop();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackToWork?.Invoke();
    private void Snooze_Click(object sender, RoutedEventArgs e) => Snoozed?.Invoke();
    private void Break_Click(object sender, RoutedEventArgs e) => TakeBreak?.Invoke();
    private void NotDistraction_Click(object sender, RoutedEventArgs e) => NotDistraction?.Invoke();
}
