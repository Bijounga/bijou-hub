using System.Windows;
using System.Windows.Controls;
using BijouHub.Services;

namespace BijouHub.Views;

// Shown when a session ends: what got done, in plain facts.
public partial class WinsWindow : Window
{
    // Whether the box "Show this after sessions" was left ticked.
    public bool KeepShowingAfter => KeepShowing.IsChecked == true;

    public WinsWindow(WinsSummary summary, string? context)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Headline.Text = summary.Headline;
        Context.Text = context ?? "";
        Context.Visibility = string.IsNullOrEmpty(context) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var line in summary.Lines)
            LinesPanel.Children.Add(new TextBlock { Text = line, FontSize = 14, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
