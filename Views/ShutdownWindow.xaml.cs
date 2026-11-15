using System.Windows;
using System.Windows.Controls;
using BijouHub.Services;

namespace BijouHub.Views;

// End of the day: what got done, a line about what went well, the first thing for tomorrow, and the
// work apps still open (closed politely, so anything unsaved still asks).
public partial class ShutdownWindow : Window
{
    public string WentWell => DidBox.Text.Trim();
    public string FirstThing => NextBox.Text.Trim();
    public List<string> AppsToClose { get; } = new();

    public ShutdownWindow(WinsSummary summary, string? nextTarget, List<string> openApps)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Headline.Text = summary.Headline;
        foreach (var line in summary.Lines)
            LinesPanel.Children.Add(new TextBlock { Text = line, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });

        NextHint.Text = nextTarget == null
            ? "It goes on tomorrow's list."
            : $"It goes on tomorrow's list and on {nextTarget}'s Up next card.";

        if (openApps.Count > 0) AppsSection.Visibility = Visibility.Visible;
        foreach (var app in openApps)
            AppsPanel.Children.Add(new CheckBox { Content = app, IsChecked = true, Margin = new Thickness(0, 0, 0, 4), Tag = app });
        Loaded += (_, _) => DidBox.Focus();
    }

    private void WrapUp_Click(object sender, RoutedEventArgs e)
    {
        foreach (var child in AppsPanel.Children.OfType<CheckBox>())
            if (child.IsChecked == true && child.Tag is string name) AppsToClose.Add(name);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
