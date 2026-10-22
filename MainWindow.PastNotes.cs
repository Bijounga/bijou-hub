using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub;

// Notes for today start empty each day. The earlier days' notes aren't thrown away: they're listed
// under today's, greyed, each with a ✕ to delete it once it's no longer needed.
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
        PastNotesPanel.Visibility = past.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var plan in past) PastNotesList.Children.Add(BuildPastNote(plan));
    }

    private UIElement BuildPastNote(DailyPlan plan)
    {
        var day = DueText.Day(plan.Date) ?? DateTime.Today;
        var heading = new TextBlock { Text = DayHeading(day), FontSize = 10, Margin = new Thickness(0, 0, 0, 2) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "FaintTextBrush");
        var text = new TextBlock { Text = plan.Notes!.Trim(), FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 110, TextTrimming = TextTrimming.CharacterEllipsis };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");

        var delete = new Button
        {
            Content = new TextBlock { Text = "✕", FontSize = 11 }, Cursor = Cursors.Hand, Focusable = false, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 0, 0, 0), ToolTip = "Delete this note",
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        ((TextBlock)delete.Content).SetResourceReference(TextBlock.ForegroundProperty, "FaintTextBrush");
        System.Windows.Automation.AutomationProperties.SetName(delete, "Delete note from " + day.ToString("MMM d", CultureInfo.InvariantCulture));
        delete.Click += (_, _) =>
        {
            plan.Notes = null;
            _dailyStore.SaveDay(plan);
            RefreshPastNotes();
        };

        var body = new StackPanel { Children = { heading, text } };
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(body);
        Grid.SetColumn(delete, 1);
        row.Children.Add(delete);

        var card = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 8, 7), Opacity = 0.75, Child = row };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "SoftBorderBrush");
        return card;
    }
}
