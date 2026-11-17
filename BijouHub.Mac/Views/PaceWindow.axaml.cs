using Avalonia.Controls;
using Avalonia.Interactivity;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

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

    public PaceWindow() : this("", null) { }

    public PaceWindow(string task, TaskPace? existing)
    {
        InitializeComponent();
        TaskText.Text = task;
        foreach (var (label, _) in Rhythms) RhythmBox.Items.Add(label);
        RhythmBox.SelectedIndex = existing == null ? 0 : Array.FindIndex(Rhythms, r => r.Rhythm == existing.Rhythm);
        AmountBox.Text = DurationText.Format(existing?.Minutes ?? 60);
        RemoveButton.IsVisible = existing != null;
        Opened += (_, _) =>
        {
            AmountBox.Focus();
            AmountBox.SelectAll();
        };
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (DurationText.TryParseMinutes(AmountBox.Text) is not int minutes)
        {
            await PromptWindow.Notice(this, "Pace", "How much? Like 1h, 45 or 1:30.");
            AmountBox.Focus();
            return;
        }
        Minutes = minutes;
        Rhythm = Rhythms[Math.Max(0, RhythmBox.SelectedIndex)].Rhythm;
        Close(true);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        Remove = true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
