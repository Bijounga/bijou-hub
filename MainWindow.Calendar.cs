using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub;

// The Calendar page: a month grid of what's due, finished and worked on each day, and a Timeline
// that lays the same things out as a day-by-day line running from last week into the next few.
public partial class MainWindow
{
    private enum CalendarMode { Month, Timeline }

    private CalendarMode _calMode = CalendarMode.Month;
    private DateTime _calMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _calDay = DateTime.Today;

    private void NavCalendar_Click(object sender, RoutedEventArgs e) => ShowCalendar();

    private void ShowCalendar()
    {
        HideAllPanels();
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = null;
        CalendarPanel.Visibility = Visibility.Visible;
        FadeIn(CalendarPanel);
        _detailProject = null;
        UpdateNotesPanelVisibility();
        SetNavHighlight();

        _calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _calDay = DateTime.Today;
        RefreshCalendarPage();
        _ = PullThenRefreshCalendarAsync();
    }

    // Fresh tasks from Google can add due dates, so draw again once they're in.
    private async Task PullThenRefreshCalendarAsync()
    {
        await RefreshGoogleGoalsAsync();
        if (CalendarPanel.Visibility == Visibility.Visible) RefreshCalendarPage();
    }

    private string? ProjectColor(string? projectId) =>
        projectId == null ? null : _projects.FirstOrDefault(p => p.Id == projectId)?.ChannelColor;

    private List<ActivityEntry> CalendarEntries(DateTime from, DateTime to) =>
        ActivityCalendar.Build(_dailyGoals, _dailyStore.LoadAll(), _logService.GetAll(), ProjectColor, from, to, DateTime.Now);

    private void RefreshCalendarPage()
    {
        if (CalendarPanel.Visibility != Visibility.Visible) return;
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

    private static Brush? HexBrush(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return null; }
    }

    private static void Themed(FrameworkElement element, DependencyProperty property, string key) => element.SetResourceReference(property, key);

    private Button CalPill(string text, bool selected, Action click, string automationName)
    {
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
        Themed(label, TextBlock.ForegroundProperty, selected ? "AccentBrush" : "TextBrush");
        var pill = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(13, 4, 13, 4), Child = label };
        Themed(pill, Border.BackgroundProperty, selected ? "CardHoverBrush" : "CardBrush");
        Themed(pill, Border.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");
        var button = new Button
        {
            Content = pill, Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0), Focusable = false,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        System.Windows.Automation.AutomationProperties.SetName(button, automationName);
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

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

        var grid = new Grid { Margin = new Thickness(0, 0, 18, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var r = 0; r < 6; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (var c = 0; c < 7; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = new TextBlock
            {
                Text = days[c].ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant(), FontSize = 10, Margin = new Thickness(6, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Themed(name, TextBlock.ForegroundProperty, "MutedTextBrush");
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
        var number = new TextBlock { Text = day.Day.ToString(), FontSize = 12, FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 3) };
        Themed(number, TextBlock.ForegroundProperty, isToday ? "AccentBrush" : inMonth ? "TextBrush" : "FaintTextBrush");
        content.Children.Add(number);

        var shown = 0;
        foreach (var entry in entries.Where(e => e.Kind != ActivityKind.Session).Take(3))
        {
            content.Children.Add(MiniLine(entry));
            shown++;
        }
        var seconds = entries.Where(e => e.Kind == ActivityKind.Session).Sum(e => e.Seconds);
        if (seconds > 0)
        {
            var worked = new TextBlock { Text = "▶ " + ActivityCalendar.Duration(seconds), FontSize = 10, Margin = new Thickness(0, 1, 0, 0) };
            Themed(worked, TextBlock.ForegroundProperty, "AccentBrush");
            content.Children.Add(worked);
        }
        var tasks = entries.Count(e => e.Kind != ActivityKind.Session);
        if (tasks > shown)
        {
            var more = new TextBlock { Text = $"+{tasks - shown} more", FontSize = 10 };
            Themed(more, TextBlock.ForegroundProperty, "MutedTextBrush");
            content.Children.Add(more);
        }

        var box = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(7, 5, 7, 4), Margin = new Thickness(0, 0, 4, 4), Child = content, Opacity = inMonth ? 1 : 0.55 };
        Themed(box, Border.BackgroundProperty, selected ? "CardHoverBrush" : "CardBrush");
        Themed(box, Border.BorderBrushProperty, selected || isToday ? "AccentBrush" : "SoftBorderBrush");

        var button = new Button
        {
            Content = box, Cursor = Cursors.Hand, Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{day.ToString("MMM d", CultureInfo.InvariantCulture)}, {entries.Count} items");
        button.Click += (_, _) =>
        {
            _calDay = day.Date;
            if (day.Month != _calMonth.Month) _calMonth = new DateTime(day.Year, day.Month, 1);
            RefreshCalendarPage();
        };
        return button;
    }

    // One task on a day cell: a dot in its channel's color and its title.
    private UIElement MiniLine(ActivityEntry entry)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 0) };
        var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        if (HexBrush(entry.Color) is { } color) dot.Fill = color;
        else Themed(dot, Shape.FillProperty, entry.Kind == ActivityKind.Done ? "SuccessBrush" : "MutedTextBrush");
        row.Children.Add(dot);
        var title = new TextBlock
        {
            Text = entry.Title, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = entry.Kind == ActivityKind.Done ? TextDecorations.Strikethrough : null
        };
        Themed(title, TextBlock.ForegroundProperty, entry.Overdue ? "DangerBrush" : entry.Kind == ActivityKind.Done ? "MutedTextBrush" : "TextBrush");
        row.Children.Add(title);
        return row;
    }

    private static string DayHeading(DateTime day)
    {
        var label = DueText.DayLabel(day, DateTime.Today);
        return label is "Today" or "Tomorrow" or "Yesterday" ? $"{label} · {day.ToString("MMM d", CultureInfo.CurrentCulture)}" : day.ToString("dddd, MMM d", CultureInfo.CurrentCulture);
    }

    // The selected day, in full.
    private UIElement BuildDayDetail(DateTime day, List<ActivityEntry> entries)
    {
        var panel = new StackPanel();
        var heading = new TextBlock { Text = DayHeading(day), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        Themed(heading, TextBlock.ForegroundProperty, "TextBrush");
        panel.Children.Add(heading);

        if (entries.Count == 0)
        {
            var none = new TextBlock { Text = "Nothing on this day.", FontSize = 12 };
            Themed(none, TextBlock.ForegroundProperty, "MutedTextBrush");
            panel.Children.Add(none);
        }
        foreach (var entry in entries) panel.Children.Add(BuildEntryRow(entry));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14), Child = scroll, VerticalAlignment = VerticalAlignment.Top, MaxHeight = 520 };
        Themed(card, Border.BackgroundProperty, "CardBrush");
        Themed(card, Border.BorderBrushProperty, "SoftBorderBrush");
        return card;
    }

    private static string Glyph(ActivityKind kind) => kind switch { ActivityKind.Done => "✓", ActivityKind.Session => "▶", _ => "○" };

    private UIElement BuildEntryRow(ActivityEntry entry)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var glyph = new TextBlock { Text = Glyph(entry.Kind), FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        if (HexBrush(entry.Color) is { } color) glyph.Foreground = color;
        else Themed(glyph, TextBlock.ForegroundProperty, entry.Kind == ActivityKind.Done ? "SuccessBrush" : entry.Kind == ActivityKind.Session ? "AccentBrush" : "MutedTextBrush");
        row.Children.Add(glyph);

        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        var title = new TextBlock
        {
            Text = entry.Title, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            TextDecorations = entry.Kind == ActivityKind.Done ? TextDecorations.Strikethrough : null
        };
        Themed(title, TextBlock.ForegroundProperty, entry.Overdue ? "DangerBrush" : "TextBrush");
        text.Children.Add(title);

        var bits = new List<string>();
        if (entry.At is { } at && (entry.Kind != ActivityKind.Due || at.TimeOfDay != TimeSpan.Zero))
            bits.Add(at.ToString("h:mm tt", CultureInfo.InvariantCulture));
        if (entry.Kind == ActivityKind.Session) bits.Add(ActivityCalendar.Duration(entry.Seconds));
        if (entry.Kind == ActivityKind.Done) bits.Add("done");
        if (entry.Overdue) bits.Add("overdue");
        if (!string.IsNullOrEmpty(entry.Detail)) bits.Add(entry.Detail);
        if (bits.Count > 0)
        {
            var sub = new TextBlock { Text = string.Join(" · ", bits), FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Themed(sub, TextBlock.ForegroundProperty, "MutedTextBrush");
            text.Children.Add(sub);
        }
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
        FrameworkElement? todayStop = null;
        var stops = byDay.Keys.Append(today).Distinct().OrderBy(d => d).ToList();
        for (var i = 0; i < stops.Count; i++)
        {
            var day = stops[i];
            var stop = BuildTimelineStop(day, byDay.GetValueOrDefault(day) ?? new List<ActivityEntry>(), i == stops.Count - 1);
            if (day == today) todayStop = stop;
            stack.Children.Add(stop);
        }

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        CalendarBody.Children.Add(scroll);
        if (todayStop != null)
            scroll.Loaded += (_, _) =>
            {
                scroll.UpdateLayout();
                scroll.ScrollToVerticalOffset(Math.Max(0, todayStop.TranslatePoint(new Point(0, 0), stack).Y - 24));
            };
    }

    private FrameworkElement BuildTimelineStop(DateTime day, List<ActivityEntry> entries, bool last)
    {
        var isToday = day == DateTime.Today;
        var stop = new Grid();
        stop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        stop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        stop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dayLabel = DueText.DayLabel(day, DateTime.Today);
        var label = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        var name = new TextBlock
        {
            Text = dayLabel is "Today" or "Tomorrow" or "Yesterday" ? dayLabel : day.ToString("dddd", CultureInfo.CurrentCulture),
            FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right
        };
        Themed(name, TextBlock.ForegroundProperty, isToday ? "AccentBrush" : "TextBrush");
        var date = new TextBlock { Text = day.ToString("MMM d", CultureInfo.CurrentCulture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        Themed(date, TextBlock.ForegroundProperty, "MutedTextBrush");
        label.Children.Add(name);
        label.Children.Add(date);
        stop.Children.Add(label);

        var rail = new Grid();
        Grid.SetColumn(rail, 1);
        if (!last)
        {
            var line = new Rectangle { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            Themed(line, Shape.FillProperty, "BorderBrush");
            rail.Children.Add(line);
        }
        var dot = new Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0), StrokeThickness = 2 };
        Themed(dot, Shape.StrokeProperty, isToday ? "AccentBrush" : "MutedTextBrush");
        Themed(dot, Shape.FillProperty, isToday ? "AccentBrush" : "BgBrush");
        rail.Children.Add(dot);
        stop.Children.Add(rail);

        var items = new StackPanel { Margin = new Thickness(6, 3, 0, 18) };
        if (entries.Count == 0)
        {
            var none = new TextBlock { Text = "Nothing yet today.", FontSize = 12 };
            Themed(none, TextBlock.ForegroundProperty, "MutedTextBrush");
            items.Children.Add(none);
        }
        foreach (var entry in entries) items.Children.Add(BuildEntryRow(entry));
        Grid.SetColumn(items, 2);
        stop.Children.Add(items);
        return stop;
    }
}
