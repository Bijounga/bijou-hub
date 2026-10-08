using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BijouHub.Mac.Controls;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// The Calendar page. Month shows what's due, done and worked each day. Week and Day are a time
// grid like Google Calendar: drag across a stretch of the day to put an event there, click one
// to edit or delete it, drag it to move it, drag its bottom edge to resize it. Your tracked work
// sessions sit on the same grid, so it doubles as a record of what you did. Timeline is the
// Notion-style view: bars across a date ruler. Same behavior as Windows.
public partial class MainWindow
{
    private enum CalendarMode { Month, Week, Day, Timeline }

    private const double HourHeight = 48;
    private const double GutterWidth = 56;
    private const double TimelineDayWidth = 44;

    private readonly EventStore _eventStore = new();
    private List<CalendarEvent> _events = new();
    private CalendarMode _calMode = CalendarMode.Month;
    private DateTime _calMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _calDay = DateTime.Today;
    private double _gridScroll = -1;
    private double _gridViewport;
    private CalendarEvent? _lastDeletedEvent;

    private void NavCalendar_Click(object? sender, RoutedEventArgs e) => ShowCalendar();

    private void ShowCalendar()
    {
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = null;
        _detailProject = null;
        ShowOnly(CalendarPanel);

        _events = _eventStore.Load();
        _calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _calDay = DateTime.Today;
        _gridScroll = -1;
        _editEvent = null;
        _eventEditorPanel = null;
        _lastDeletedEvent = null;
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
        ActivityCalendar.Build(_dailyGoals, _dailyStore.LoadAll(), _logService.GetAll(), _events, ProjectColor, from, to, DateTime.Now);

    private void RefreshCalendarPage()
    {
        if (!CalendarPanel.IsVisible) return;
        if (_eventEditorPanel?.Parent is Panel old) old.Children.Remove(_eventEditorPanel);
        CalendarBody.Children.Clear();
        CalendarControls.Children.Clear();

        if (_lastDeletedEvent != null) CalendarControls.Children.Add(CalPill("Undo delete", false, UndoDeleteEvent, "Undo delete"));
        CalendarControls.Children.Add(CalPill("＋ Event", false, NewEventFromButton, "New event", accent: true));
        CalendarControls.Children.Add(CalPill("‹", false, () => ShiftCalendar(-1), _calMode == CalendarMode.Month ? "Previous month" : _calMode == CalendarMode.Timeline ? "Scroll earlier" : _calMode == CalendarMode.Week ? "Previous week" : "Previous day"));
        CalendarControls.Children.Add(CalPill("Today", false, GoToToday, "Today"));
        CalendarControls.Children.Add(CalPill("›", false, () => ShiftCalendar(1), _calMode == CalendarMode.Month ? "Next month" : _calMode == CalendarMode.Timeline ? "Scroll later" : _calMode == CalendarMode.Week ? "Next week" : "Next day"));
        foreach (var (mode, label) in new[] { (CalendarMode.Month, "Month"), (CalendarMode.Week, "Week"), (CalendarMode.Day, "Day"), (CalendarMode.Timeline, "Timeline") })
        {
            var m = mode;
            CalendarControls.Children.Add(CalPill(label, _calMode == m, () => SetCalendarMode(m), label == "Month" ? "Calendar view" : $"{label} view"));
        }

        switch (_calMode)
        {
            case CalendarMode.Month: BuildMonthView(); break;
            case CalendarMode.Timeline: BuildTimelineView(); break;
            default: BuildTimeGridView(); break;
        }
    }

    private void SetCalendarMode(CalendarMode mode)
    {
        _calMode = mode;
        if (mode == CalendarMode.Month) _calMonth = new DateTime(_calDay.Year, _calDay.Month, 1);
        RefreshCalendarPage();
    }

    private void GoToToday()
    {
        _calDay = DateTime.Today;
        _calMonth = new DateTime(_calDay.Year, _calDay.Month, 1);
        RefreshCalendarPage();
    }

    private void ShiftCalendar(int direction)
    {
        switch (_calMode)
        {
            case CalendarMode.Month:
                _calMonth = _calMonth.AddMonths(direction);
                break;
            case CalendarMode.Week:
                _calDay = _calDay.AddDays(7 * direction);
                break;
            case CalendarMode.Day:
                _calDay = _calDay.AddDays(direction);
                break;
            case CalendarMode.Timeline:
                if (_timelineScroll is { } s)
                    s.Offset = new Vector(Math.Max(0, s.Offset.X + direction * 7 * TimelineDayWidth), s.Offset.Y);
                return;
        }
        RefreshCalendarPage();
    }

    // ---------- Shared bits ----------

    private static IBrush? HexBrush(string? hex, double opacity = 1)
    {
        if (string.IsNullOrEmpty(hex) || !Color.TryParse(hex, out var color)) return null;
        return new SolidColorBrush(color, opacity);
    }

    private Button CalPill(string text, bool selected, Action click, string automationName, bool accent = false)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = text, FontSize = 12, FontWeight = selected || accent ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = Brush(selected || accent ? "AccentBrush" : "TextBrush")
            },
            Padding = new Thickness(13, 4),
            Margin = new Thickness(6, 0, 0, 0),
            CornerRadius = new CornerRadius(14),
            Background = Brush(selected ? "CardHoverBrush" : "CardBrush"),
            BorderBrush = Brush(selected || accent ? "AccentBrush" : "BorderBrush"),
            BorderThickness = new Thickness(1)
        };
        Avalonia.Automation.AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => click();
        return button;
    }

    private static string DayHeading(DateTime day)
    {
        var label = DueText.DayLabel(day, DateTime.Today);
        return label is "Today" or "Tomorrow" or "Yesterday" ? $"{label} · {day.ToString("MMM d", CultureInfo.CurrentCulture)}" : day.ToString("dddd, MMM d", CultureInfo.CurrentCulture);
    }

    private static string Clock(DateTime time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);
    private static string Clock(DateTime day, int minute) => day.Date.AddMinutes(minute).ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static string Glyph(ActivityKind kind) => kind switch { ActivityKind.Done => "✓", ActivityKind.Session => "▶", ActivityKind.Event => "■", _ => "○" };

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

    // One thing on a day cell: a dot in its color and its title.
    private Control MiniLine(ActivityEntry entry)
    {
        var dot = new Ellipse
        {
            Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center,
            Fill = HexBrush(entry.Color) ?? Brush(entry.Kind == ActivityKind.Done ? "SuccessBrush" : "MutedTextBrush")
        };
        var title = new TextBlock
        {
            Text = entry.Title, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 90,
            TextDecorations = entry.Kind == ActivityKind.Done ? TextDecorations.Strikethrough : null,
            Foreground = Brush(entry.Overdue ? "DangerBrush" : entry.Kind == ActivityKind.Done ? "MutedTextBrush" : "TextBrush")
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 1, 0, 0), Children = { dot, title } };
    }

    // The selected day, in full.
    private Control BuildDayDetail(DateTime day, List<ActivityEntry> entries)
    {
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var open = new Button { Content = "Open day ›", Padding = new Thickness(8, 2) };
        DockPanel.SetDock(open, Dock.Right);
        Avalonia.Automation.AutomationProperties.SetName(open, "Open day");
        open.Click += (_, _) => { _calDay = day; SetCalendarMode(CalendarMode.Day); };
        head.Children.Add(open);
        head.Children.Add(new TextBlock { Text = DayHeading(day), FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);

        if (entries.Count == 0)
            panel.Children.Add(new TextBlock { Text = "Nothing on this day.", FontSize = 12, Foreground = Brush("MutedTextBrush") });
        foreach (var entry in entries) panel.Children.Add(BuildEntryRow(entry));

        var add = new Button { Content = "＋ Add event", Padding = new Thickness(10, 5), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        Avalonia.Automation.AutomationProperties.SetName(add, "Add event on this day");
        add.Click += (_, _) => StartNewEvent(day.AddHours(9), 60);
        panel.Children.Add(add);

        return new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14), VerticalAlignment = VerticalAlignment.Top, MaxHeight = 520,
            Background = Brush("CardBrush"), BorderBrush = Brush("SoftBorderBrush"),
            Child = new ScrollViewer { Content = panel }
        };
    }

    private Control BuildEntryRow(ActivityEntry entry)
    {
        var glyph = new TextBlock
        {
            Text = Glyph(entry.Kind), FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0),
            Foreground = HexBrush(entry.Color) ?? Brush(entry.Kind == ActivityKind.Done ? "SuccessBrush" : entry.Kind == ActivityKind.Session ? "AccentBrush" : "MutedTextBrush")
        };

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = entry.Title, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            TextDecorations = entry.Kind == ActivityKind.Done ? TextDecorations.Strikethrough : null,
            Foreground = Brush(entry.Overdue ? "DangerBrush" : "TextBrush")
        });

        var bits = new List<string>();
        if (entry.Kind == ActivityKind.Event && entry.At is { } start)
            bits.Add($"{Clock(start)} – {Clock(start.AddSeconds(entry.Seconds))}");
        else if (entry.At is { } at && (entry.Kind != ActivityKind.Due || at.TimeOfDay != TimeSpan.Zero))
            bits.Add(Clock(at));
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

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*"), Margin = new Thickness(0, 0, 0, 9), Background = Brushes.Transparent };
        row.Children.Add(glyph);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        // An event opens in the editor (on its day).
        if (entry.Kind == ActivityKind.Event && entry.SourceId != null)
        {
            var id = entry.SourceId;
            var open = new Button
            {
                Content = row, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            Avalonia.Automation.AutomationProperties.SetName(open, "Edit event " + entry.Title);
            open.Click += (_, _) =>
            {
                if (_events.FirstOrDefault(e => e.Id == id) is { } ev) OpenEventEditor(ev.Clone(), false);
            };
            return open;
        }
        return row;
    }

    // ---------- Day / Week time grid ----------

    private List<DateTime> GridDays()
    {
        if (_calMode == CalendarMode.Day) return new List<DateTime> { _calDay.Date };
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var start = _calDay.Date.AddDays(-(((int)_calDay.DayOfWeek - (int)first + 7) % 7));
        return Enumerable.Range(0, 7).Select(i => start.AddDays(i)).ToList();
    }

    private Canvas? _surface;
    private TimeGridGesture? _gesture;
    private GridGeometry? _geometry;
    private List<DateTime> _gridDays = new();
    private List<ActivityEntry> _gridEntries = new();
    private Border? _ghost;
    private TextBlock? _ghostText;

    private void BuildTimeGridView()
    {
        _gridDays = GridDays();
        var first = _gridDays[0];
        var last = _gridDays[^1];
        CalendarTitle.Text = _calMode == CalendarMode.Day
            ? _calDay.ToString("dddd, MMM d, yyyy", CultureInfo.CurrentCulture)
            : first.Month == last.Month
                ? $"{first.ToString("MMM d", CultureInfo.CurrentCulture)} – {last.Day}, {last.Year}"
                : $"{first.ToString("MMM d", CultureInfo.CurrentCulture)} – {last.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";
        _gridEntries = CalendarEntries(first, last);

        var page = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };

        // Day headings, and due tasks with no time of day in a strip under them.
        var columns = string.Join(",", _gridDays.Select(_ => "*"));
        var head = new Grid { Margin = new Thickness(GutterWidth, 0, 14, 4), ColumnDefinitions = new ColumnDefinitions(columns) };
        var strip = new Grid { Margin = new Thickness(GutterWidth, 0, 14, 6), ColumnDefinitions = new ColumnDefinitions(columns) };
        for (var i = 0; i < _gridDays.Count; i++)
        {
            var day = _gridDays[i];
            var isToday = day == DateTime.Today;
            var heading = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = day.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush(isToday ? "AccentBrush" : "MutedTextBrush") },
                    new TextBlock { Text = day.Day.ToString(), FontSize = 18, FontWeight = isToday ? FontWeight.SemiBold : FontWeight.Normal, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush(isToday ? "AccentBrush" : "TextBrush") }
                }
            };
            Grid.SetColumn(heading, i);
            head.Children.Add(heading);

            var chips = new StackPanel { Margin = new Thickness(2, 0) };
            var untimed = _gridEntries.Where(e => e.Day == day && e.Kind == ActivityKind.Due && (e.At == null || e.At.Value.TimeOfDay == TimeSpan.Zero)).ToList();
            foreach (var task in untimed.Take(2))
                chips.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1), Margin = new Thickness(0, 0, 0, 2), Background = Brush("CardBrush"),
                    Child = new TextBlock { Text = "○ " + task.Title, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush(task.Overdue ? "DangerBrush" : "TextBrush") }
                });
            if (untimed.Count > 2)
                chips.Children.Add(new TextBlock { Text = $"+{untimed.Count - 2} more", FontSize = 10, Margin = new Thickness(4, 0, 0, 0), Foreground = Brush("MutedTextBrush") });
            Grid.SetColumn(chips, i);
            strip.Children.Add(chips);
        }
        main.Children.Add(head);
        Grid.SetRow(strip, 1);
        main.Children.Add(strip);

        // Hours down the side, the grid itself beside them.
        var body = new Grid { Height = HourHeight * 24, ColumnDefinitions = new ColumnDefinitions($"{GutterWidth},*") };
        var gutter = new Canvas();
        for (var hour = 1; hour < 24; hour++)
        {
            var label = new TextBlock { Text = DateTime.Today.AddHours(hour).ToString("h tt", CultureInfo.InvariantCulture), FontSize = 10, Foreground = Brush("MutedTextBrush"), Width = GutterWidth - 8, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, hour * HourHeight - 7);
            gutter.Children.Add(label);
        }
        body.Children.Add(gutter);

        _surface = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };
        Grid.SetColumn(_surface, 1);
        body.Children.Add(_surface);
        _surface.SizeChanged += (_, _) => RenderSurface();
        _surface.PointerPressed += Surface_PointerPressed;
        _surface.PointerMoved += Surface_PointerMoved;
        _surface.PointerReleased += Surface_PointerReleased;
        _surface.PointerCaptureLost += (_, _) => _gesture?.Cancel();

        var scroll = new ScrollViewer { Content = body };
        Grid.SetRow(scroll, 2);
        main.Children.Add(scroll);
        var startOffset = _gridScroll >= 0 ? _gridScroll
            : _gridDays.Contains(DateTime.Today) ? Math.Max(0, (DateTime.Now.Hour - 1) * HourHeight) : 7 * HourHeight;
        scroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            scroll.UpdateLayout();
            scroll.Offset = new Vector(0, startOffset);
        }, DispatcherPriority.Background);
        scroll.ScrollChanged += (_, _) =>
        {
            if (scroll.Offset.Y > 0 || _gridScroll >= 0) _gridScroll = scroll.Offset.Y;
            _gridViewport = scroll.Viewport.Height;
        };

        page.Children.Add(main);
        if (_editEvent != null)
        {
            var editor = _eventEditorPanel ?? BuildEventEditor();
            editor.Margin = new Thickness(18, 0, 0, 0);
            Grid.SetColumn(editor, 1);
            page.Children.Add(editor);
        }
        CalendarBody.Children.Add(page);
    }

    // Lays every block of the grid out for the surface's current size.
    private void RenderSurface()
    {
        if (_surface == null || _surface.Bounds.Width < 10) return;
        var canvas = _surface;
        canvas.Children.Clear();
        _ghost = null;
        var width = canvas.Bounds.Width;
        var geometry = new GridGeometry(_gridDays.Count, width, HourHeight);
        var columnWidth = geometry.ColumnWidth;

        // Rules: an hour line each hour, a lighter one at the half hour, a divider between days.
        for (var hour = 0; hour < 24; hour++)
        {
            var line = new Rectangle { Width = width, Height = 1, Fill = Brush("SoftBorderBrush"), IsHitTestVisible = false };
            Canvas.SetTop(line, hour * HourHeight);
            canvas.Children.Add(line);
            var half = new Rectangle { Width = width, Height = 1, Opacity = 0.35, Fill = Brush("SoftBorderBrush"), IsHitTestVisible = false };
            Canvas.SetTop(half, hour * HourHeight + HourHeight / 2);
            canvas.Children.Add(half);
        }
        for (var d = 0; d < _gridDays.Count; d++)
        {
            if (_gridDays[d] == DateTime.Today)
            {
                var tint = new Rectangle { Width = columnWidth, Height = HourHeight * 24, Opacity = 0.07, Fill = Brush("AccentBrush"), IsHitTestVisible = false };
                Canvas.SetLeft(tint, d * columnWidth);
                canvas.Children.Add(tint);
            }
            if (d == 0) continue;
            var divider = new Rectangle { Width = 1, Height = HourHeight * 24, Fill = Brush("SoftBorderBrush"), IsHitTestVisible = false };
            Canvas.SetLeft(divider, d * columnWidth);
            canvas.Children.Add(divider);
        }

        // Everything on each day, side by side where it overlaps.
        var blocks = new List<(int Day, int Start, int End, ActivityEntry? Entry, CalendarEvent? Event, bool Draft)>();
        for (var d = 0; d < _gridDays.Count; d++)
        {
            var day = _gridDays[d];
            foreach (var entry in _gridEntries.Where(e => e.Day == day && e.At != null))
            {
                if (entry.Kind == ActivityKind.Due && entry.At!.Value.TimeOfDay == TimeSpan.Zero) continue;
                var start = (int)entry.At!.Value.TimeOfDay.TotalMinutes;
                var end = entry.Kind is ActivityKind.Session or ActivityKind.Event ? Math.Min(24 * 60, start + Math.Max(15, entry.Seconds / 60)) : Math.Min(24 * 60, start + 30);
                if (entry.Kind == ActivityKind.Event && _editEvent is { } editing && !_editIsNew && entry.SourceId == editing.Id) continue; // drawn from the editor's copy below
                blocks.Add((d, start, Math.Max(end, start + 15), entry, null, false));
            }
            if (_editEvent is { } ev && ev.Start.Date == day)
                blocks.Add((d, (int)ev.Start.TimeOfDay.TotalMinutes, Math.Max((int)ev.Start.TimeOfDay.TotalMinutes + 15, (int)(ev.End - day).TotalMinutes), null, ev, _editIsNew));
        }

        var packed = new (int Lane, int Lanes)[blocks.Count];
        for (var d = 0; d < _gridDays.Count; d++)
        {
            var indexes = blocks.Select((b, i) => (b, i)).Where(x => x.b.Day == d).Select(x => x.i).ToList();
            var lanes = TimeGridLanes.Pack(indexes.Select(i => (blocks[i].Start, blocks[i].End)).ToList());
            for (var k = 0; k < indexes.Count; k++) packed[indexes[k]] = lanes[k];
        }

        var items = new List<GridItem>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var (day, start, end, entry, ev, draft) = blocks[i];
            var (lane, lanes) = packed[i];
            var laneWidth = columnWidth / lanes;
            var left = day * columnWidth + lane * laneWidth;
            Control visual = ev != null ? EventBlock(ev.Title, ev.Color, start, end, day, draft, !draft)
                : entry!.Kind == ActivityKind.Event ? EventBlock(entry.Title, entry.Color ?? EventPalette.Colors[0], start, end, day, false, false)
                : entry.Kind == ActivityKind.Session ? SessionBlock(entry, start, end)
                : TaskBlock(entry);
            visual.Width = Math.Max(8, laneWidth - 3);
            visual.Height = Math.Max(16, (end - start) * geometry.MinuteHeight - 1);
            Canvas.SetLeft(visual, left + 1);
            Canvas.SetTop(visual, geometry.YOf(start) + 0.5);
            visual.IsHitTestVisible = false; // the grid itself handles the pointer
            canvas.Children.Add(visual);

            var eventId = ev != null ? (draft ? null : ev.Id) : entry!.Kind == ActivityKind.Event ? entry.SourceId : null;
            if (eventId != null) items.Add(new GridItem(eventId, day, start, end, lane, lanes));
        }

        // Now.
        var todayIndex = _gridDays.IndexOf(DateTime.Today);
        if (todayIndex >= 0)
        {
            var y = geometry.YOf((int)DateTime.Now.TimeOfDay.TotalMinutes);
            var line = new Rectangle { Width = columnWidth, Height = 2, Fill = Brush("DangerBrush"), IsHitTestVisible = false };
            Canvas.SetLeft(line, todayIndex * columnWidth);
            Canvas.SetTop(line, y - 1);
            canvas.Children.Add(line);
            var dot = new Ellipse { Width = 9, Height = 9, Fill = Brush("DangerBrush"), IsHitTestVisible = false };
            Canvas.SetLeft(dot, todayIndex * columnWidth - 4);
            Canvas.SetTop(dot, y - 4.5);
            canvas.Children.Add(dot);
        }

        _gesture = new TimeGridGesture(geometry, items);
        _geometry = geometry;
    }

    // An event: a solid block in its color, title and time inside. Fainter while it's still a draft.
    private Control EventBlock(string title, string color, int start, int end, int day, bool draft, bool selected)
    {
        var lines = new StackPanel { Margin = new Thickness(6, 2, 4, 2) };
        lines.Children.Add(new TextBlock { Text = title.Length == 0 ? "(No title)" : title, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        if (end - start >= 40)
            lines.Children.Add(new TextBlock { Text = $"{Clock(_gridDays[day], start)} – {Clock(_gridDays[day], end)}", FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border
        {
            CornerRadius = new CornerRadius(5), Background = HexBrush(color, draft ? 0.7 : 0.95), Child = lines, ClipToBounds = true,
            BorderThickness = new Thickness(selected ? 2 : 0), BorderBrush = Brushes.White
        };
    }

    // Time you tracked: an outline in the accent color, so it reads as "what I did".
    private Control SessionBlock(ActivityEntry entry, int start, int end)
    {
        var grid = new Grid();
        grid.Children.Add(new Rectangle
        {
            RadiusX = 5, RadiusY = 5, StrokeThickness = 1.2, StrokeDashArray = new AvaloniaList<double> { 3, 2 },
            Stroke = Brush("AccentBrush"), Fill = Brush("CardBrush"), Opacity = 0.9
        });
        var lines = new StackPanel { Margin = new Thickness(6, 2, 4, 2) };
        lines.Children.Add(new TextBlock { Text = "▶ " + entry.Title, FontSize = 11.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("AccentBrush") });
        if (end - start >= 40)
            lines.Children.Add(new TextBlock
            {
                Text = $"{ActivityCalendar.Duration(entry.Seconds)} tracked" + (string.IsNullOrEmpty(entry.Detail) ? "" : " · " + entry.Detail),
                FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("MutedTextBrush")
            });
        grid.Children.Add(lines);
        return grid;
    }

    private Control TaskBlock(ActivityEntry entry) => new Border
    {
        CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), ClipToBounds = true,
        Background = Brush("CardHoverBrush"), BorderBrush = Brush("BorderBrush"),
        Child = new TextBlock
        {
            Text = Glyph(entry.Kind) + " " + entry.Title, FontSize = 11, Margin = new Thickness(5, 1, 4, 1), TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = entry.Kind == ActivityKind.Done ? TextDecorations.Strikethrough : null,
            Foreground = Brush(entry.Overdue ? "DangerBrush" : entry.Kind == ActivityKind.Done ? "MutedTextBrush" : "TextBrush")
        }
    };

    // ---------- Pointer on the grid ----------

    private void Surface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_gesture == null || _surface == null || !e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(_surface);
        _gesture.Begin(point.X, point.Y);
        e.Pointer.Capture(_surface);
        e.Handled = true;
    }

    private void Surface_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_gesture == null || _surface == null || _geometry == null) return;
        var point = e.GetPosition(_surface);
        if (!_gesture.Active)
        {
            _surface.Cursor = new Cursor(_gesture.HitTest(point.X, point.Y, out _) switch
            {
                GridTarget.ResizeHandle => StandardCursorType.SizeNorthSouth,
                GridTarget.Body => StandardCursorType.Hand,
                _ => StandardCursorType.Arrow
            });
            return;
        }
        if (_gesture.Move(point.X, point.Y) is { } state) ShowGhost(state);
    }

    private void Surface_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_gesture == null || _surface == null || !_gesture.Active) return;
        var point = e.GetPosition(_surface);
        var result = _gesture.End(point.X, point.Y);
        e.Pointer.Capture(null);
        RemoveGhost();
        ApplyGesture(result);
        e.Handled = true;
    }

    // The block being dragged out, moved or resized, drawn as the pointer travels.
    private void ShowGhost(GestureState state)
    {
        if (_surface == null || _geometry == null) return;
        if (_ghost == null)
        {
            _ghostText = new TextBlock { FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, Margin = new Thickness(6, 2, 4, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            _ghost = new Border { CornerRadius = new CornerRadius(5), IsHitTestVisible = false, Child = _ghostText, ClipToBounds = true, BorderThickness = new Thickness(1.5), BorderBrush = Brushes.White };
            _surface.Children.Add(_ghost);
        }
        var color = state.Id != null && _events.FirstOrDefault(ev => ev.Id == state.Id) is { } existing ? existing.Color : _newEventColor;
        _ghost.Background = HexBrush(color, 0.75);
        _ghostText!.Text = $"{Clock(_gridDays[state.Day], state.StartMinute)} – {Clock(_gridDays[state.Day], state.EndMinute)}";
        var columnWidth = _geometry.ColumnWidth;
        _ghost.Width = Math.Max(8, columnWidth - 3);
        _ghost.Height = Math.Max(10, (state.EndMinute - state.StartMinute) * _geometry.MinuteHeight);
        Canvas.SetLeft(_ghost, state.Day * columnWidth + 1);
        Canvas.SetTop(_ghost, _geometry.YOf(state.StartMinute));
    }

    private void RemoveGhost()
    {
        if (_ghost != null) _surface?.Children.Remove(_ghost);
        _ghost = null;
    }

    private void ApplyGesture(GestureState result)
    {
        switch (result.Kind)
        {
            case GestureKind.Create:
            {
                var day = _gridDays[result.Day];
                StartNewEvent(day.AddMinutes(result.StartMinute), result.EndMinute - result.StartMinute);
                break;
            }
            case GestureKind.Click:
                if (_events.FirstOrDefault(ev => ev.Id == result.Id) is { } clicked) OpenEventEditor(clicked.Clone(), false);
                break;
            case GestureKind.Move:
            case GestureKind.Resize:
            {
                if (_events.FirstOrDefault(ev => ev.Id == result.Id) is not { } ev) break;
                var day = _gridDays[result.Day];
                ev.Start = day.AddMinutes(result.StartMinute);
                ev.End = day.AddMinutes(result.EndMinute);
                _eventStore.Save(_events);
                if (_editEvent?.Id == ev.Id) OpenEventEditor(ev.Clone(), false);
                else RefreshCalendarPage();
                break;
            }
        }
    }

    // ---------- Event editor ----------

    private CalendarEvent? _editEvent;
    private bool _editIsNew;
    private Border? _eventEditorPanel;
    private string _newEventColor = EventPalette.Colors[0];
    private TextBox? _evTitle, _evStart, _evEnd, _evNotes;
    private TextBlock? _evDateText, _evError;
    private DateTime _evDate;
    private string _evColor = EventPalette.Colors[0];
    private StackPanel? _evSwatches;

    private void NewEventFromButton()
    {
        var day = _calDay.Date;
        var hour = day == DateTime.Today ? Math.Min(23, DateTime.Now.Hour + 1) : 9;
        StartNewEvent(day.AddHours(hour), 60);
    }

    private void StartNewEvent(DateTime start, int minutes)
    {
        var end = start.AddMinutes(Math.Max(15, minutes));
        if (end.Date != start.Date) end = start.Date.AddDays(1);
        OpenEventEditor(new CalendarEvent { Start = start, End = end, Color = _newEventColor }, true);
    }

    private void OpenEventEditor(CalendarEvent ev, bool isNew)
    {
        _editEvent = ev;
        _editIsNew = isNew;
        _evDate = ev.Start.Date;
        _evColor = ev.Color;
        _eventEditorPanel = null; // a fresh panel for this event

        // The grid shows events on their own day, so go there.
        if (_calMode is CalendarMode.Month or CalendarMode.Timeline) _calMode = CalendarMode.Day;
        if (!_gridDays.Contains(ev.Start.Date) || _calMode == CalendarMode.Day) _calDay = ev.Start.Date;
        ScrollToEvent(ev);
        RefreshCalendarPage();
        Dispatcher.UIThread.Post(() => { _evTitle?.Focus(); _evTitle?.SelectAll(); }, DispatcherPriority.Input);
    }

    // Brings an event into view if the grid is scrolled to other hours.
    private void ScrollToEvent(CalendarEvent ev)
    {
        var top = ev.Start.TimeOfDay.TotalHours * HourHeight;
        if (_gridViewport > 0 && (top < _gridScroll || top + 60 > _gridScroll + _gridViewport)) _gridScroll = Math.Max(0, top - 2 * HourHeight);
    }

    private void CloseEventEditor()
    {
        _editEvent = null;
        _eventEditorPanel = null;
        RefreshCalendarPage();
    }

    private static void SetId(Control control, string id, string? name = null)
    {
        Avalonia.Automation.AutomationProperties.SetAutomationId(control, id);
        if (name != null) Avalonia.Automation.AutomationProperties.SetName(control, name);
    }

    private Border BuildEventEditor()
    {
        var ev = _editEvent!;
        var panel = new StackPanel { Spacing = 0 };

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = new Button { Content = "✕", Padding = new Thickness(8, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        DockPanel.SetDock(close, Dock.Right);
        Avalonia.Automation.AutomationProperties.SetName(close, "Close event editor");
        close.Click += (_, _) => CloseEventEditor();
        head.Children.Add(close);
        head.Children.Add(new TextBlock { Text = _editIsNew ? "New event" : "Edit event", FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);

        _evTitle = new TextBox { Text = ev.Title, FontSize = 15, Padding = new Thickness(8, 6), Margin = new Thickness(0, 0, 0, 12), Watermark = "Add title" };
        SetId(_evTitle, "EventTitleBox", "Event title");
        _evTitle.TextChanged += (_, _) => LiveUpdateEditor();
        _evTitle.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { SaveEvent(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CloseEventEditor(); e.Handled = true; }
        };
        panel.Children.Add(_evTitle);

        // Date: ‹ Thursday, Oct 8 ›
        var prev = new Button { Content = "‹", Padding = new Thickness(8, 2) };
        Avalonia.Automation.AutomationProperties.SetName(prev, "Previous day for event");
        var next = new Button { Content = "›", Padding = new Thickness(8, 2) };
        Avalonia.Automation.AutomationProperties.SetName(next, "Next day for event");
        _evDateText = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        SetId(_evDateText, "EventDateText");
        UpdateEventDateText();
        prev.Click += (_, _) => { _evDate = _evDate.AddDays(-1); UpdateEventDateText(); LiveUpdateEditor(); };
        next.Click += (_, _) => { _evDate = _evDate.AddDays(1); UpdateEventDateText(); LiveUpdateEditor(); };
        var dateRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetColumn(_evDateText, 1);
        Grid.SetColumn(next, 2);
        dateRow.Children.Add(prev);
        dateRow.Children.Add(_evDateText);
        dateRow.Children.Add(next);
        panel.Children.Add(dateRow);

        // Start – end
        _evStart = new TextBox { Text = Clock(ev.Start), Padding = new Thickness(8, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        SetId(_evStart, "EventStartBox", "Event start time");
        _evStart.TextChanged += (_, _) => LiveUpdateEditor();
        _evEnd = new TextBox { Text = Clock(ev.End), Padding = new Thickness(8, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        SetId(_evEnd, "EventEndBox", "Event end time");
        _evEnd.TextChanged += (_, _) => LiveUpdateEditor();
        var dash = new TextBlock { Text = "–", Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("MutedTextBrush") };
        var times = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetColumn(dash, 1);
        Grid.SetColumn(_evEnd, 2);
        times.Children.Add(_evStart);
        times.Children.Add(dash);
        times.Children.Add(_evEnd);
        panel.Children.Add(times);

        // Colors
        _evSwatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        BuildSwatches();
        panel.Children.Add(_evSwatches);

        panel.Children.Add(new TextBlock { Text = "NOTES", FontSize = 10, Margin = new Thickness(0, 0, 0, 4), Foreground = Brush("MutedTextBrush") });
        _evNotes = new TextBox { Text = ev.Notes ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, MaxHeight = 120, Padding = new Thickness(8, 5), Margin = new Thickness(0, 0, 0, 10) };
        SetId(_evNotes, "EventNotesBox", "Event notes");
        panel.Children.Add(_evNotes);

        _evError = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), IsVisible = false, Foreground = Brush("DangerBrush") };
        SetId(_evError, "EventErrorText");
        panel.Children.Add(_evError);

        var save = new Button { Content = "Save", MinWidth = 84, Padding = new Thickness(12, 6), Classes = { "primary" }, HorizontalContentAlignment = HorizontalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(save, "Save event");
        save.Click += (_, _) => SaveEvent();
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(save, 2);
        if (!_editIsNew)
        {
            var delete = new Button { Content = "Delete", Padding = new Thickness(12, 6) };
            Avalonia.Automation.AutomationProperties.SetName(delete, "Delete event");
            delete.Click += (_, _) => DeleteEvent();
            buttons.Children.Add(delete);
        }
        buttons.Children.Add(save);
        panel.Children.Add(buttons);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16), Width = 300, VerticalAlignment = VerticalAlignment.Top,
            Background = Brush("CardBrush"), BorderBrush = Brush("SoftBorderBrush"), Child = panel
        };
        _eventEditorPanel = card;
        return card;
    }

    // The block on the grid follows what's typed, so you see the event take shape before saving.
    private void LiveUpdateEditor()
    {
        if (_editEvent == null || _evTitle == null || _evStart == null || _evEnd == null) return;
        _editEvent.Title = _evTitle.Text ?? "";
        _editEvent.Color = _evColor;
        var start = DueText.ParseTime(_evStart.Text);
        var end = DueText.ParseTime(_evEnd.Text);
        if (start != null && end != null && (end.Value > start.Value || end.Value == TimeOnly.MinValue && start.Value > TimeOnly.MinValue))
        {
            _editEvent.Start = _evDate.Date.Add(start.Value.ToTimeSpan());
            _editEvent.End = end.Value == TimeOnly.MinValue ? _evDate.Date.AddDays(1) : _evDate.Date.Add(end.Value.ToTimeSpan());
        }
        RenderSurface();
    }

    private void UpdateEventDateText()
    {
        if (_evDateText != null) _evDateText.Text = _evDate.ToString("dddd, MMM d", CultureInfo.CurrentCulture);
    }

    private void BuildSwatches()
    {
        if (_evSwatches == null) return;
        _evSwatches.Children.Clear();
        foreach (var color in EventPalette.Colors)
        {
            var selected = string.Equals(color, _evColor, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 0), Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                Background = Brushes.Transparent, BorderThickness = new Thickness(2), BorderBrush = selected ? Brushes.White : Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Content = new Ellipse { Width = 16, Height = 16, Fill = HexBrush(color) }
            };
            ToolTip.SetTip(button, EventPalette.Name(color));
            Avalonia.Automation.AutomationProperties.SetName(button, $"Event color {EventPalette.Name(color)}");
            button.Click += (_, _) =>
            {
                _evColor = color;
                _newEventColor = color;
                BuildSwatches();
                LiveUpdateEditor();
            };
            _evSwatches.Children.Add(button);
        }
    }

    private void SaveEvent()
    {
        if (_editEvent == null || _evTitle == null || _evStart == null || _evEnd == null) return;
        var start = DueText.ParseTime(_evStart.Text);
        var end = DueText.ParseTime(_evEnd.Text);
        string? error = null;
        if (start == null) error = "Start time not understood. Try 9am or 14:30.";
        else if (end == null) error = "End time not understood. Try 10:30am or 15:00.";
        else if (end.Value <= start.Value && !(end.Value == TimeOnly.MinValue && start.Value > TimeOnly.MinValue)) error = "The end has to come after the start.";
        if (error != null)
        {
            _evError!.Text = error;
            _evError.IsVisible = true;
            return;
        }

        var ev = _editEvent;
        ev.Title = (_evTitle.Text ?? "").Trim();
        ev.Start = _evDate.Date.Add(start!.Value.ToTimeSpan());
        ev.End = end!.Value == TimeOnly.MinValue ? _evDate.Date.AddDays(1) : _evDate.Date.Add(end.Value.ToTimeSpan()); // 12am as an end means midnight
        ev.Color = _evColor;
        ev.Notes = string.IsNullOrWhiteSpace(_evNotes?.Text) ? null : _evNotes!.Text!.Trim();

        var index = _events.FindIndex(x => x.Id == ev.Id);
        if (index >= 0) _events[index] = ev;
        else _events.Add(ev);
        _eventStore.Save(_events);
        _calDay = ev.Start.Date;
        _lastDeletedEvent = null;

        ScrollToEvent(ev);
        CloseEventEditor();
    }

    private void DeleteEvent()
    {
        if (_editEvent == null) return;
        var id = _editEvent.Id;
        _lastDeletedEvent = _events.FirstOrDefault(x => x.Id == id);
        _events.RemoveAll(x => x.Id == id);
        _eventStore.Save(_events);
        CloseEventEditor();
    }

    private void UndoDeleteEvent()
    {
        if (_lastDeletedEvent == null) return;
        _events.Add(_lastDeletedEvent);
        _eventStore.Save(_events);
        _calDay = _lastDeletedEvent.Start.Date;
        _lastDeletedEvent = null;
        RefreshCalendarPage();
    }

    // ---------- Timeline (Notion-style) ----------

    private ScrollViewer? _timelineScroll;

    // Bars across a date ruler: tasks from when they were added to when they're due, events on
    // their day, and the days you worked. A red line marks today.
    private void BuildTimelineView()
    {
        CalendarTitle.Text = "Timeline";
        var today = DateTime.Today;
        var from = today.AddDays(-14);
        var to = today.AddDays(60);
        var days = (to - from).Days + 1;
        var sections = TimelineModel.Build(_dailyGoals, _dailyStore.LoadAll(), _logService.GetAll(), _events, ProjectColor, from, to, DateTime.Now);
        var red = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));

        const double rowHeight = 38;
        const double sectionHeight = 34;
        var height = 12.0;
        foreach (var section in sections) height += sectionHeight + section.Rows.Count * rowHeight + 10;
        height = Math.Max(height, 200);
        var width = days * TimelineDayWidth;

        // The ruler: month names, then each day (today in a red circle).
        var ruler = new Canvas { Width = width, Height = 54, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var monthLabels = new List<double>();
        for (var i = 0; i < days; i++)
        {
            var day = from.AddDays(i);
            if (i == 0 || day.Day == 1)
            {
                var month = new TextBlock { Text = day.ToString(i == 0 || day.Month == 1 ? "MMMM yyyy" : "MMMM", CultureInfo.CurrentCulture), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brush("TextBrush") };
                Canvas.SetLeft(month, i * TimelineDayWidth + 6);
                ruler.Children.Add(month);
                monthLabels.Add(i * TimelineDayWidth + 6);
            }
            var isToday = day == today;
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var holder = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = isToday ? red : null,
                Child = new TextBlock
                {
                    Text = day.Day.ToString(), FontSize = 12, FontWeight = isToday ? FontWeight.Bold : FontWeight.Normal, TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = isToday ? Brushes.White : Brush(weekend ? "FaintTextBrush" : "MutedTextBrush")
                }
            };
            Canvas.SetLeft(holder, i * TimelineDayWidth + (TimelineDayWidth - 24) / 2);
            Canvas.SetTop(holder, 26);
            ruler.Children.Add(holder);
        }
        // The month of the day at the left edge, kept in view; hidden while a month's own label is there.
        var sticky = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brush("TextBrush") };
        ruler.Children.Add(sticky);
        var rulerScroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = ruler };

        // The body.
        var canvas = new Canvas { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        for (var i = 0; i < days; i++)
        {
            var day = from.AddDays(i);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                var band = new Rectangle { Width = TimelineDayWidth, Height = height, Opacity = 0.035, Fill = Brush("TextBrush"), IsHitTestVisible = false };
                Canvas.SetLeft(band, i * TimelineDayWidth);
                canvas.Children.Add(band);
            }
        }
        var headings = new List<TextBlock>();
        var y = 10.0;
        foreach (var section in sections)
        {
            var title = new TextBlock { Text = $"{section.Title}  {section.Rows.Count}", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brush("MutedTextBrush") };
            Canvas.SetTop(title, y + 8);
            Canvas.SetLeft(title, 8);
            canvas.Children.Add(title);
            headings.Add(title);
            y += sectionHeight;
            foreach (var row in section.Rows)
            {
                foreach (var bar in row.Bars) AddTimelineBar(canvas, bar, from, y, rowHeight, width, bar == row.Bars[^1], red);
                y += rowHeight;
            }
            y += 10;
        }
        var todayX = (today - from).Days * TimelineDayWidth + TimelineDayWidth / 2;
        var todayLine = new Rectangle { Width = 2, Height = height, Fill = red, IsHitTestVisible = false };
        Canvas.SetLeft(todayLine, todayX - 1);
        canvas.Children.Add(todayLine);
        if (sections.Count == 0)
        {
            var empty = new TextBlock { Text = "Nothing on the timeline yet. Add an event or give a task a date.", FontSize = 13, Foreground = Brush("MutedTextBrush") };
            Canvas.SetLeft(empty, todayX + 20);
            Canvas.SetTop(empty, 20);
            canvas.Children.Add(empty);
        }

        var bodyScroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = canvas };
        void OnScrolled()
        {
            var offset = bodyScroll.Offset.X;
            rulerScroll.Offset = new Vector(offset, 0);
            foreach (var heading in headings) Canvas.SetLeft(heading, offset + 8); // section titles stay in view
            var leftDay = from.AddDays((int)Math.Min(days - 1, offset / TimelineDayWidth));
            sticky.Text = leftDay.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            Canvas.SetLeft(sticky, offset + 6);
            sticky.IsVisible = !monthLabels.Any(x => x >= offset - 4 && x < offset + 170);
        }
        bodyScroll.ScrollChanged += (_, _) => OnScrolled();
        bodyScroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            bodyScroll.UpdateLayout();
            bodyScroll.Offset = new Vector(Math.Max(0, todayX - 220), 0);
            OnScrolled();
        }, DispatcherPriority.Background);
        _timelineScroll = bodyScroll;

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(rulerScroll);
        Grid.SetRow(bodyScroll, 1);
        layout.Children.Add(bodyScroll);
        CalendarBody.Children.Add(layout);
    }

    private void AddTimelineBar(Canvas canvas, TimelineBar bar, DateTime from, double y, double rowHeight, double canvasWidth, bool last, IBrush red)
    {
        var x = Math.Max(0, (bar.Start - from).Days) * TimelineDayWidth;
        var right = ((bar.End - from).Days + 1) * TimelineDayWidth;
        var barWidth = Math.Max(TimelineDayWidth - 4, Math.Min(right, canvasWidth) - x - 4);
        var barHeight = rowHeight - 10;
        var work = bar.Kind == TimelineKind.Work;

        var fill = HexBrush(bar.Color, bar.Done ? 0.35 : 0.9);
        var pill = new Border { Width = barWidth, Height = barHeight, CornerRadius = new CornerRadius(7), Background = fill };
        if (fill == null)
        {
            pill.Background = Brush(bar.Overdue ? "DangerBrush" : work ? "CardHoverBrush" : "AccentBrush");
            pill.Opacity = bar.Done ? 0.4 : 0.9;
        }
        if (bar.Overdue && fill != null)
        {
            pill.BorderBrush = red;
            pill.BorderThickness = new Thickness(2);
        }

        var inside = work ? bar.Detail ?? "" : barWidth >= 140 ? bar.Label : "";
        if (inside.Length > 0)
        {
            var text = new TextBlock
            {
                Text = inside, FontSize = work ? 10 : 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Margin = work ? new Thickness(1, 0) : new Thickness(8, 0, 6, 0), TextAlignment = work ? TextAlignment.Center : TextAlignment.Left,
                TextTrimming = work ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                TextDecorations = bar.Done ? TextDecorations.Strikethrough : null,
                Foreground = bar.Done ? Brush("MutedTextBrush") : fill != null ? Brushes.White : Brush(work ? "TextBrush" : "BgBrush")
            };
            pill.Child = text;
        }
        Canvas.SetLeft(pill, x + 2);
        Canvas.SetTop(pill, y + 5);
        canvas.Children.Add(pill);

        // The name beside the bar when it doesn't fit inside (and, for work, after the last day).
        var outside = work ? (last ? bar.Label : "") : inside.Length == 0 ? bar.Label : "";
        if (outside.Length > 0)
        {
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            name.Children.Add(new TextBlock
            {
                Text = outside, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(bar.Done ? "MutedTextBrush" : "TextBrush"), TextDecorations = bar.Done ? TextDecorations.Strikethrough : null
            });
            if (!string.IsNullOrEmpty(bar.Detail) && !work)
                name.Children.Add(new TextBlock { Text = bar.Detail, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("MutedTextBrush") });
            Canvas.SetLeft(name, x + barWidth + 10);
            Canvas.SetTop(name, y + rowHeight / 2 - 9);
            canvas.Children.Add(name);
        }

        if (bar.Kind == TimelineKind.Event && bar.SourceId != null)
        {
            var id = bar.SourceId;
            pill.Cursor = new Cursor(StandardCursorType.Hand);
            pill.PointerReleased += (_, _) => { if (_events.FirstOrDefault(e => e.Id == id) is { } ev) OpenEventEditor(ev.Clone(), false); };
        }
    }
}
