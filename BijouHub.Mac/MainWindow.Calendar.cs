using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BijouHub.Mac.Controls;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// The Calendar page: a month grid of what's due, finished and worked on each day, and a Timeline
// that lays the same things out as a day-by-day line running from last week into the next few.
// Same behavior as Windows.
public partial class MainWindow
{
    private enum CalendarMode { Month, Timeline }

    private CalendarMode _calMode = CalendarMode.Month;
    private DateTime _calMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _calDay = DateTime.Today;

    private void NavCalendar_Click(object? sender, RoutedEventArgs e) => ShowCalendar();

    private void ShowCalendar()
    {
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = null;
        _detailProject = null;
        ShowOnly(CalendarPanel);

        _calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _calDay = DateTime.Today;
        RefreshCalendarPage();
        _ = PullThenRefreshCalendarAsync();
    }

    // Fresh tasks from Google can add due dates, so draw again once they're in.
    private async Task PullThenRefreshCalendarAsync()
    {
        await RefreshGoogleGoalsAsync();
        if (CalendarPanel.IsVisible) RefreshCalendarPage();
    }

    private string? ProjectColor(string? projectId) =>
        projectId == null ? null : _projects.FirstOrDefault(p => p.Id == projectId)?.ChannelColor;

    private List<ActivityEntry> CalendarEntries(DateTime from, DateTime to) =>
        ActivityCalendar.Build(_dailyGoals, _dailyStore.LoadAll(), _logService.GetAll(), ProjectColor, from, to, DateTime.Now);

    private void RefreshCalendarPage()
    {
        if (!CalendarPanel.IsVisible) return;
        CalendarBody.Children.Clear();
        CalendarControls.Children.Clear();

        if (_calMode == CalendarMode.Month)
        {
            CalendarControls.Children.Add(CalPill("‹", false, () => { _calMonth = _calMonth.AddMonths(-1); RefreshCalendarPage(); }, "Previous month"));
            CalendarControls.Children.Add(CalPill("Today", false, () =>
            {
                _calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                _calDay = DateTime.Today;
                RefreshCalendarPage();
            }, "Today"));
            CalendarControls.Children.Add(CalPill("›", false, () => { _calMonth = _calMonth.AddMonths(1); RefreshCalendarPage(); }, "Next month"));
        }
        CalendarControls.Children.Add(CalPill("Month", _calMode == CalendarMode.Month, () => { _calMode = CalendarMode.Month; RefreshCalendarPage(); }, "Calendar view"));
        CalendarControls.Children.Add(CalPill("Timeline", _calMode == CalendarMode.Timeline, () => { _calMode = CalendarMode.Timeline; RefreshCalendarPage(); }, "Timeline view"));

        if (_calMode == CalendarMode.Month) BuildMonthView();
        else BuildTimelineView();
    }

    private IBrush? CalendarHex(string? hex) => HexBrushConverter.Parse(hex);

    private Button CalPill(string text, bool selected, Action click, string automationName)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, FontSize = 12, FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Brush(selected ? "AccentBrush" : "TextBrush") },
            Padding = new Thickness(13, 4),
            Margin = new Thickness(6, 0, 0, 0),
            CornerRadius = new CornerRadius(14),
            Background = Brush(selected ? "CardHoverBrush" : "CardBrush"),
            BorderBrush = Brush(selected ? "AccentBrush" : "BorderBrush"),
            BorderThickness = new Thickness(1)
        };
        Avalonia.Automation.AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => click();
        return button;
    }

    // ---------- Month ----------

    private void BuildMonthView()
    {
        CalendarTitle.Text = _calMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var days = ActivityCalendar.MonthGrid(_calMonth, first);
        var entries = CalendarEntries(days[0], days[^1]).GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.ToList());

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,280") };
        var grid = new Grid { Margin = new Thickness(0, 0, 18, 0), RowDefinitions = new RowDefinitions("Auto,*,*,*,*,*,*"), ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*") };
        for (var c = 0; c < 7; c++)
        {
            var name = new TextBlock
            {
                Text = days[c].ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant(), FontSize = 10, Margin = new Thickness(6, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Left, Foreground = Brush("MutedTextBrush")
            };
            Grid.SetColumn(name, c);
            grid.Children.Add(name);
        }
        for (var i = 0; i < 42; i++)
        {
            var cell = BuildDayCell(days[i], entries.GetValueOrDefault(days[i]) ?? new List<ActivityEntry>());
            Grid.SetRow(cell, 1 + i / 7);
            Grid.SetColumn(cell, i % 7);
            grid.Children.Add(cell);
        }
        layout.Children.Add(grid);

        var detail = BuildDayDetail(_calDay, entries.GetValueOrDefault(_calDay.Date) ?? new List<ActivityEntry>());
        Grid.SetColumn(detail, 1);
        layout.Children.Add(detail);
        CalendarBody.Children.Add(layout);
    }

    private Button BuildDayCell(DateTime day, List<ActivityEntry> entries)
    {
        var inMonth = day.Month == _calMonth.Month;
        var isToday = day.Date == DateTime.Today;
        var selected = day.Date == _calDay.Date;

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = day.Day.ToString(), FontSize = 12, FontWeight = isToday ? FontWeight.Bold : FontWeight.Normal, Margin = new Thickness(0, 0, 0, 3),
            Foreground = Brush(isToday ? "AccentBrush" : inMonth ? "TextBrush" : "FaintTextBrush")
        });

        var shown = 0;
        foreach (var entry in entries.Where(e => e.Kind != ActivityKind.Session).Take(3))
        {
            content.Children.Add(MiniLine(entry));
            shown++;
        }
        var seconds = entries.Where(e => e.Kind == ActivityKind.Session).Sum(e => e.Seconds);
        if (seconds > 0)
            content.Children.Add(new TextBlock { Text = "▶ " + ActivityCalendar.Duration(seconds), FontSize = 10, Margin = new Thickness(0, 1, 0, 0), Foreground = Brush("AccentBrush") });
        var tasks = entries.Count(e => e.Kind != ActivityKind.Session);
        if (tasks > shown)
            content.Children.Add(new TextBlock { Text = $"+{tasks - shown} more", FontSize = 10, Foreground = Brush("MutedTextBrush") });

        var box = new Border
        {
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(7, 5, 7, 4), Margin = new Thickness(0, 0, 4, 4),
            Child = content, Opacity = inMonth ? 1 : 0.55,
            Background = Brush(selected ? "CardHoverBrush" : "CardBrush"),
            BorderBrush = Brush(selected || isToday ? "AccentBrush" : "SoftBorderBrush")
        };
        var button = new Button
        {
            Content = box, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Avalonia.Automation.AutomationProperties.SetName(button, $"{day.ToString("MMM d", CultureInfo.InvariantCulture)}, {entries.Count} items");
        button.Click += (_, _) =>
        {
            _calDay = day.Date;
            if (day.Month != _calMonth.Month) _calMonth = new DateTime(day.Year, day.Month, 1);
            RefreshCalendarPage();
        };
        return button;
    }

    // One task on a day cell: a dot in its channel's color and its title.
    private Control MiniLine(ActivityEntry entry)
    {
        var dot = new Ellipse
        {
            Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center,
            Fill = CalendarHex(entry.Color) ?? Brush(entry.Kind == ActivityKind.Done ? "SuccessBrush" : "MutedTextBrush")
        };
        var title = new TextBlock
        {
            Text = entry.Title, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = entry.Kind == ActivityKind.Done ? TextDecorations.Strikethrough : null,
            Foreground = Brush(entry.Overdue ? "DangerBrush" : entry.Kind == ActivityKind.Done ? "MutedTextBrush" : "TextBrush")
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 1, 0, 0), Children = { dot, title } };
        title.MaxWidth = 90;
        return row;
    }

    private static string DayHeading(DateTime day)
    {
        var label = DueText.DayLabel(day, DateTime.Today);
        return label is "Today" or "Tomorrow" or "Yesterday" ? $"{label} · {day.ToString("MMM d", CultureInfo.CurrentCulture)}" : day.ToString("dddd, MMM d", CultureInfo.CurrentCulture);
    }

    // The selected day, in full.
    private Control BuildDayDetail(DateTime day, List<ActivityEntry> entries)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = DayHeading(day), FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        if (entries.Count == 0)
            panel.Children.Add(new TextBlock { Text = "Nothing on this day.", FontSize = 12, Foreground = Brush("MutedTextBrush") });
        foreach (var entry in entries) panel.Children.Add(BuildEntryRow(entry));

        return new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14), VerticalAlignment = VerticalAlignment.Top, MaxHeight = 520,
            Background = Brush("CardBrush"), BorderBrush = Brush("SoftBorderBrush"),
            Child = new ScrollViewer { Content = panel }
        };
    }

    private static string Glyph(ActivityKind kind) => kind switch { ActivityKind.Done => "✓", ActivityKind.Session => "▶", _ => "○" };

    private Control BuildEntryRow(ActivityEntry entry)
    {
        var glyph = new TextBlock
        {
            Text = Glyph(entry.Kind), FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0),
            Foreground = CalendarHex(entry.Color) ?? Brush(entry.Kind == ActivityKind.Done ? "SuccessBrush" : entry.Kind == ActivityKind.Session ? "AccentBrush" : "MutedTextBrush")
        };

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = entry.Title, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            TextDecorations = entry.Kind == ActivityKind.Done ? TextDecorations.Strikethrough : null,
            Foreground = Brush(entry.Overdue ? "DangerBrush" : "TextBrush")
        });

        var bits = new List<string>();
        if (entry.At is { } at && (entry.Kind != ActivityKind.Due || at.TimeOfDay != TimeSpan.Zero))
            bits.Add(at.ToString("h:mm tt", CultureInfo.InvariantCulture));
        if (entry.Kind == ActivityKind.Session) bits.Add(ActivityCalendar.Duration(entry.Seconds));
        if (entry.Kind == ActivityKind.Done) bits.Add("done");
        if (entry.Overdue) bits.Add("overdue");
        if (!string.IsNullOrEmpty(entry.Detail)) bits.Add(entry.Detail);
        if (bits.Count > 0)
            text.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", bits), FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush("MutedTextBrush")
            });

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*"), Margin = new Thickness(0, 0, 0, 9) };
        row.Children.Add(glyph);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    // ---------- Timeline ----------

    // A line down the page: last week at the top, today in the middle of it, the next weeks
    // below. Only days with something on them get a stop (today always does).
    private void BuildTimelineView()
    {
        CalendarTitle.Text = "Timeline";
        var today = DateTime.Today;
        var byDay = CalendarEntries(today.AddDays(-7), today.AddDays(28)).GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.ToList());

        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 24) };
        Control? todayStop = null;
        var stops = byDay.Keys.Append(today).Distinct().OrderBy(d => d).ToList();
        for (var i = 0; i < stops.Count; i++)
        {
            var day = stops[i];
            var stop = BuildTimelineStop(day, byDay.GetValueOrDefault(day) ?? new List<ActivityEntry>(), i == stops.Count - 1);
            if (day == today) todayStop = stop;
            stack.Children.Add(stop);
        }

        var scroll = new ScrollViewer { Content = stack };
        CalendarBody.Children.Add(scroll);
        if (todayStop != null)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                scroll.UpdateLayout();
                var point = todayStop.TranslatePoint(new Point(0, 0), stack);
                if (point is { } p) scroll.Offset = new Vector(0, Math.Max(0, p.Y - 24));
            }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private Control BuildTimelineStop(DateTime day, List<ActivityEntry> entries, bool last)
    {
        var isToday = day == DateTime.Today;
        var stop = new Grid { ColumnDefinitions = new ColumnDefinitions("130,26,*") };

        var dayLabel = DueText.DayLabel(day, DateTime.Today);
        var label = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        label.Children.Add(new TextBlock
        {
            Text = dayLabel is "Today" or "Tomorrow" or "Yesterday" ? dayLabel : day.ToString("dddd", CultureInfo.CurrentCulture),
            FontSize = 14, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = Brush(isToday ? "AccentBrush" : "TextBrush")
        });
        label.Children.Add(new TextBlock { Text = day.ToString("MMM d", CultureInfo.CurrentCulture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Foreground = Brush("MutedTextBrush") });
        stop.Children.Add(label);

        var rail = new Grid();
        Grid.SetColumn(rail, 1);
        if (!last)
            rail.Children.Add(new Rectangle { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), Fill = Brush("BorderBrush") });
        rail.Children.Add(new Ellipse
        {
            Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0), StrokeThickness = 2,
            Stroke = Brush(isToday ? "AccentBrush" : "MutedTextBrush"), Fill = Brush(isToday ? "AccentBrush" : "BgBrush")
        });
        stop.Children.Add(rail);

        var items = new StackPanel { Margin = new Thickness(6, 3, 0, 18) };
        if (entries.Count == 0)
            items.Children.Add(new TextBlock { Text = "Nothing yet today.", FontSize = 12, Foreground = Brush("MutedTextBrush") });
        foreach (var entry in entries) items.Children.Add(BuildEntryRow(entry));
        Grid.SetColumn(items, 2);
        stop.Children.Add(items);
        return stop;
    }
}
