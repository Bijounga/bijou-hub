using System.Windows;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

// Sets how much to put into a task each day or week, or takes the pace off it.
public partial class PaceWindow : Window
{
    private static readonly (string Label, PaceRhythm Rhythm)[] Rhythms =
    {
        ("Every day", PaceRhythm.Daily), ("Weekdays only", PaceRhythm.Weekdays), ("Once a week", PaceRhythm.Weekly)
    };

    public int Minutes { get; private set; }
    public PaceRhythm Rhythm { get; private set; }
    public bool Remove { get; private set; }

    public PaceWindow(string task, TaskPace? existing)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        TaskText.Text = task;
        foreach (var (label, _) in Rhythms) RhythmBox.Items.Add(label);
        RhythmBox.SelectedIndex = existing == null ? 0 : Array.FindIndex(Rhythms, r => r.Rhythm == existing.Rhythm);
        AmountBox.Text = DurationText.Format(existing?.Minutes ?? 60);
        RemoveButton.Visibility = existing == null ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) =>
        {
            AmountBox.Focus();
            AmountBox.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DurationText.TryParseMinutes(AmountBox.Text) is not int minutes)
        {
            MessageBox.Show("How much? Like 1h, 45 or 1:30.", "Pace", MessageBoxButton.OK, MessageBoxImage.Information);
            AmountBox.Focus();
            return;
        }
        Minutes = minutes;
        Rhythm = Rhythms[Math.Max(0, RhythmBox.SelectedIndex)].Rhythm;
        DialogResult = true;
        Close();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        Remove = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
