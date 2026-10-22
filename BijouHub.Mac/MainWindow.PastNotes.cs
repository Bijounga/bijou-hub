using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Notes for today start empty each day. The earlier days' notes aren't thrown away: they're listed
// under today's, greyed, each with a ✕ to delete it once it's no longer needed. Same behavior as Windows.
public partial class MainWindow
{
    private const int PastNotesShown = 30;

    private void RefreshPastNotes()
    {
        var today = DailyPlanStore.Key(DateTime.Today);
        var past = _dailyStore.LoadAll()
            .Where(p => string.CompareOrdinal(p.Date, today) < 0 && !string.IsNullOrWhiteSpace(p.Notes))
            .OrderByDescending(p => p.Date, StringComparer.Ordinal)
            .Take(PastNotesShown)
            .ToList();

        PastNotesList.Children.Clear();
        PastNotesPanel.IsVisible = past.Count > 0;
        foreach (var plan in past) PastNotesList.Children.Add(BuildPastNote(plan));
    }

    private Control BuildPastNote(DailyPlan plan)
    {
        var day = DueText.Day(plan.Date) ?? DateTime.Today;
        var body = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = DayHeading(day), FontSize = 10, Margin = new Thickness(0, 0, 0, 2), Foreground = Brush("FaintTextBrush") },
                new TextBlock { Text = plan.Notes!.Trim(), FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("MutedTextBrush") }
            }
        };

        var delete = new Button
        {
            Content = new TextBlock { Text = "✕", FontSize = 11, Foreground = Brush("FaintTextBrush") }, Classes = { "ghost" }, Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0)
        };
        ToolTip.SetTip(delete, "Delete this note");
        Avalonia.Automation.AutomationProperties.SetName(delete, "Delete note from " + day.ToString("MMM d", CultureInfo.InvariantCulture));
        delete.Click += (_, _) =>
        {
            plan.Notes = null;
            _dailyStore.SaveDay(plan);
            RefreshPastNotes();
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(delete, 1);
        row.Children.Add(body);
        row.Children.Add(delete);

        return new Border
        {
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 8, 7), Margin = new Thickness(0, 0, 0, 6), Opacity = 0.75,
            Background = Brush("CardBrush"), BorderBrush = Brush("SoftBorderBrush"), Child = row
        };
    }
}
