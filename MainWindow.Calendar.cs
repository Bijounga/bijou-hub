using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub;

// The Calendar page. Month shows what's due, done and worked each day. Week and Day are a time
// grid like Google Calendar: drag across a stretch of the day to put an event there, click one
// to edit or delete it, drag it to move it, drag its bottom edge to resize it. Your tracked work
// sessions sit on the same grid, so it doubles as a record of what you did. Timeline is the
// Notion-style view: bars across a date ruler.
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

        _events = _eventStore.Load();
        _calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _calDay = DateTime.Today;
        _gridScroll = -1;
        _editEvent = null;
        _lastDeletedEvent = null;
        RefreshCalendarPage();
        _ = PullThenRefreshCalendarAsync();
    }

    // Fresh tasks from Google can add due dates, so draw again once they're in (unless an event
    // is being edited: redrawing is fine, the editor keeps what's typed).
    private async Task PullThenRefreshCalendarAsync()
    {
        await RefreshGoogleGoalsAsync();
        if (CalendarPanel.Visibility == Visibility.Visible) RefreshCalendarPage();
    }

    private string? ProjectColor(string? projectId) =>
        projectId == null ? null : _projects.FirstOrDefault(p => p.Id == projectId)?.ChannelColor;

    private List<ActivityEntry> CalendarEntries(DateTime from, DateTime to) =>
        ActivityCalendar.Build(_dailyGoals, _dailyStore.LoadAll(), _logService.GetAll(), _events, ProjectColor, from, to, DateTime.Now);

    private void RefreshCalendarPage()
    {
        if (CalendarPanel.Visibility != Visibility.Visible) return;
        _eventEditorPanel?.Let(p => (p.Parent as Panel)?.Children.Remove(p));
        CalendarBody.Children.Clear();
        CalendarControls.Children.Clear();

        // Right to left in the header: view switch, then ‹ Today ›, then + Event.
        foreach (var (mode, label) in new[] { (CalendarMode.Month, "Month"), (CalendarMode.Week, "Week"), (CalendarMode.Day, "Day"), (CalendarMode.Timeline, "Timeline") })
        {
            var m = mode;
            CalendarControls.Children.Add(CalPill(label, _calMode == m, () => SetCalendarMode(m), label == "Month" ? "Calendar view" : $"{label} view"));
        }
        CalendarControls.Children.Insert(0, CalPill("＋ Event", false, () => NewEventFromButton(), "New event", accent: true));
        CalendarControls.Children.Insert(1, CalPill("›", false, () => ShiftCalendar(1), _calMode == CalendarMode.Month ? "Next month" : _calMode == CalendarMode.Timeline ? "Scroll later" : _calMode == CalendarMode.Week ? "Next week" : "Next day"));
        CalendarControls.Children.Insert(1, CalPill("Today", false, GoToToday, "Today"));
        CalendarControls.Children.Insert(1, CalPill("‹", false, () => ShiftCalendar(-1), _calMode == CalendarMode.Month ? "Previous month" : _calMode == CalendarMode.Timeline ? "Scroll earlier" : _calMode == CalendarMode.Week ? "Previous week" : "Previous day"));
        if (_lastDeletedEvent != null)
            CalendarControls.Children.Insert(0, CalPill("Undo delete", false, UndoDeleteEvent, "Undo delete"));

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
                _timelineScroll?.Let(s => s.ScrollToHorizontalOffset(Math.Max(0, s.HorizontalOffset + direction * 7 * TimelineDayWidth)));
                return;
        }
        RefreshCalendarPage();
    }

    // ---------- Shared bits ----------

    private static Brush? HexBrush(string? hex, double opacity = 1)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    private static void Themed(FrameworkElement element, DependencyProperty property, string key) => element.SetResourceReference(property, key);

    private Button CalPill(string text, bool selected, Action click, string automationName, bool accent = false)
    {
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = selected || accent ? FontWeights.SemiBold : FontWeights.Normal };
        Themed(label, TextBlock.ForegroundProperty, selected || accent ? "AccentBrush" : "TextBrush");
        var pill = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(13, 4, 13, 4), Child = label };
        Themed(pill, Border.BackgroundProperty, selected ? "CardHoverBrush" : "CardBrush");
        Themed(pill, Border.BorderBrushProperty, selected || accent ? "AccentBrush" : "BorderBrush");
        var button = new Button
        {
            Content = pill, Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0), Focusable = false,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        System.Windows.Automation.AutomationProperties.SetName(button, automationName);
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

    // One thing on a day cell: a dot in its color and its title.
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

    // The selected day, in full.
    private UIElement BuildDayDetail(DateTime day, List<ActivityEntry> entries)
    {
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var open = new Button { Content = "Open day ›", Padding = new Thickness(8, 2, 8, 2), Cursor = Cursors.Hand };
        open.SetValue(DockPanel.DockProperty, Dock.Right);
        System.Windows.Automation.AutomationProperties.SetName(open, "Open day");
        open.Click += (_, _) => { _calDay = day; SetCalendarMode(CalendarMode.Day); };
        head.Children.Add(open);
        var heading = new TextBlock { Text = DayHeading(day), FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Themed(heading, TextBlock.ForegroundProperty, "TextBrush");
        head.Children.Add(heading);
        panel.Children.Add(head);

        if (entries.Count == 0)
        {
            var none = new TextBlock { Text = "Nothing on this day.", FontSize = 12 };
            Themed(none, TextBlock.ForegroundProperty, "MutedTextBrush");
            panel.Children.Add(none);
        }
        foreach (var entry in entries) panel.Children.Add(BuildEntryRow(entry));

        var add = new Button { Content = "＋ Add event", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(add, "Add event on this day");
        add.Click += (_, _) => StartNewEvent(day.AddHours(9), 60);
        panel.Children.Add(add);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14), Child = scroll, VerticalAlignment = VerticalAlignment.Top, MaxHeight = 520 };
        Themed(card, Border.BackgroundProperty, "CardBrush");
        Themed(card, Border.BorderBrushProperty, "SoftBorderBrush");
        return card;
    }

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
        if (entry.Kind == ActivityKind.Event && entry.At is { } start)
            bits.Add($"{Clock(start)} – {Clock(start.AddSeconds(entry.Seconds))}");
        else if (entry.At is { } at && (entry.Kind != ActivityKind.Due || at.TimeOfDay != TimeSpan.Zero))
            bits.Add(Clock(at));
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

        // An event opens in the editor (on its day).
        if (entry.Kind == ActivityKind.Event && entry.SourceId != null)
        {
            var id = entry.SourceId;
            var open = new Button
            {
                Content = row, Cursor = Cursors.Hand, Focusable = true, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
            };
            System.Windows.Automation.AutomationProperties.SetName(open, "Edit event " + entry.Title);
            open.Click += (_, _) =>
            {
                if (_events.FirstOrDefault(e => e.Id == id) is { } ev) OpenEventEditor(ev.Clone(), false);
            };
            return open;
        }
        return row;
    }

    // ---------- Day / Week time grid ----------

    private sealed record GridBlock(int Day, int Start, int End, FrameworkElement Visual, string? EventId);

    private List<DateTime> GridDays()
    {
        if (_calMode == CalendarMode.Day) return new List<DateTime> { _calDay.Date };
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var start = _calDay.Date.AddDays(-(((int)_calDay.DayOfWeek - (int)first + 7) % 7));
        return Enumerable.Range(0, 7).Select(i => start.AddDays(i)).ToList();
    }

    private Canvas? _surface;
    private TimeGridGesture? _gesture;
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

        var page = new Grid();
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Day headings, and due tasks with no time of day in a strip under them.
        var scrollbar = SystemParameters.VerticalScrollBarWidth;
        var head = new Grid { Margin = new Thickness(GutterWidth, 0, scrollbar, 4) };
        var strip = new Grid { Margin = new Thickness(GutterWidth, 0, scrollbar, 6) };
        for (var i = 0; i < _gridDays.Count; i++)
        {
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var day = _gridDays[i];
            var isToday = day == DateTime.Today;
            var number = new TextBlock { Text = day.Day.ToString(), FontSize = 18, FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center };
            Themed(number, TextBlock.ForegroundProperty, isToday ? "AccentBrush" : "TextBrush");
            var name = new TextBlock { Text = day.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
            Themed(name, TextBlock.ForegroundProperty, isToday ? "AccentBrush" : "MutedTextBrush");
            var heading = new StackPanel { Children = { name, number } };
            Grid.SetColumn(heading, i);
            head.Children.Add(heading);

            var chips = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
            var untimed = _gridEntries.Where(e => e.Day == day && e.Kind == ActivityKind.Due && (e.At == null || e.At.Value.TimeOfDay == TimeSpan.Zero)).ToList();
            foreach (var task in untimed.Take(2))
            {
                var chip = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(0, 0, 0, 2) };
                Themed(chip, Border.BackgroundProperty, "CardBrush");
                var label = new TextBlock { Text = "○ " + task.Title, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
                Themed(label, TextBlock.ForegroundProperty, task.Overdue ? "DangerBrush" : "TextBrush");
                chip.Child = label;
                chips.Children.Add(chip);
            }
            if (untimed.Count > 2)
            {
                var more = new TextBlock { Text = $"+{untimed.Count - 2} more", FontSize = 10, Margin = new Thickness(4, 0, 0, 0) };
                Themed(more, TextBlock.ForegroundProperty, "MutedTextBrush");
                chips.Children.Add(more);
            }
            Grid.SetColumn(chips, i);
            strip.Children.Add(chips);
        }
        main.Children.Add(head);
        Grid.SetRow(strip, 1);
        main.Children.Add(strip);

        // Hours down the side, the grid itself beside them.
        var body = new Grid { Height = HourHeight * 24 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GutterWidth) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var gutter = new Canvas();
        for (var hour = 1; hour < 24; hour++)
        {
            var label = new TextBlock { Text = DateTime.Today.AddHours(hour).ToString("h tt", CultureInfo.InvariantCulture), FontSize = 10 };
            Themed(label, TextBlock.ForegroundProperty, "MutedTextBrush");
            Canvas.SetRight(label, 8);
            Canvas.SetTop(label, hour * HourHeight - 7);
            gutter.Children.Add(label);
        }
        body.Children.Add(gutter);

        _surface = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };
        Grid.SetColumn(_surface, 1);
        body.Children.Add(_surface);
        _surface.SizeChanged += (_, _) => RenderSurface();
        _surface.MouseLeftButtonDown += Surface_MouseDown;
        _surface.MouseMove += Surface_MouseMove;
        _surface.MouseLeftButtonUp += Surface_MouseUp;
        _surface.LostMouseCapture += (_, _) => _gesture?.Cancel();

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        Grid.SetRow(scroll, 2);
        main.Children.Add(scroll);
        var startOffset = _gridScroll >= 0 ? _gridScroll
            : _gridDays.Contains(DateTime.Today) ? Math.Max(0, (DateTime.Now.Hour - 1) * HourHeight) : 7 * HourHeight;
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(startOffset);
        scroll.ScrollChanged += (_, e) =>
        {
            if (e.VerticalChange != 0) _gridScroll = scroll.VerticalOffset;
            _gridViewport = scroll.ViewportHeight;
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
        if (_surface == null || _surface.ActualWidth < 10) return;
        var canvas = _surface;
        canvas.Children.Clear();
        _ghost = null;
        var width = canvas.ActualWidth;
        var geometry = new GridGeometry(_gridDays.Count, width, HourHeight);
        var columnWidth = geometry.ColumnWidth;

        // Rules: an hour line each hour, a lighter one at the half hour, a divider between days.
        for (var hour = 0; hour < 24; hour++)
        {
            var line = new Rectangle { Width = width, Height = 1 };
            Themed(line, Shape.FillProperty, "SoftBorderBrush");
            Canvas.SetTop(line, hour * HourHeight);
            canvas.Children.Add(line);
            var half = new Rectangle { Width = width, Height = 1, Opacity = 0.35 };
            Themed(half, Shape.FillProperty, "SoftBorderBrush");
            Canvas.SetTop(half, hour * HourHeight + HourHeight / 2);
            canvas.Children.Add(half);
        }
        for (var d = 0; d < _gridDays.Count; d++)
        {
            if (_gridDays[d] == DateTime.Today)
            {
                var tint = new Rectangle { Width = columnWidth, Height = HourHeight * 24, Opacity = 0.07 };
                Themed(tint, Shape.FillProperty, "AccentBrush");
                Canvas.SetLeft(tint, d * columnWidth);
                canvas.Children.Add(tint);
            }
            if (d == 0) continue;
            var divider = new Rectangle { Width = 1, Height = HourHeight * 24 };
            Themed(divider, Shape.FillProperty, "SoftBorderBrush");
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
            var visual = ev != null ? EventBlock(ev.Title, ev.Color, start, end, day, draft, !draft)
                : entry!.Kind == ActivityKind.Event ? EventBlock(entry.Title, entry.Color ?? EventPalette.Colors[0], start, end, day, false, false)
                : entry.Kind == ActivityKind.Session ? SessionBlock(entry, start, end)
                : TaskBlock(entry);
            visual.Width = Math.Max(8, laneWidth - 3);
            visual.Height = Math.Max(16, (end - start) * geometry.MinuteHeight - 1);
            Canvas.SetLeft(visual, left + 1);
            Canvas.SetTop(visual, geometry.YOf(start) + 0.5);
            visual.IsHitTestVisible = false; // the grid itself handles the mouse
            canvas.Children.Add(visual);

            var eventId = ev != null ? (draft ? null : ev.Id) : entry!.Kind == ActivityKind.Event ? entry.SourceId : null;
            if (eventId != null) items.Add(new GridItem(eventId, day, start, end, lane, lanes));
        }

        // Now.
        var todayIndex = _gridDays.IndexOf(DateTime.Today);
        if (todayIndex >= 0)
        {
            var y = geometry.YOf((int)DateTime.Now.TimeOfDay.TotalMinutes);
            var line = new Rectangle { Width = columnWidth, Height = 2, IsHitTestVisible = false };
            Themed(line, Shape.FillProperty, "DangerBrush");
            Canvas.SetLeft(line, todayIndex * columnWidth);
            Canvas.SetTop(line, y - 1);
            canvas.Children.Add(line);
            var dot = new Ellipse { Width = 9, Height = 9, IsHitTestVisible = false };
            Themed(dot, Shape.FillProperty, "DangerBrush");
            Canvas.SetLeft(dot, todayIndex * columnWidth - 4);
            Canvas.SetTop(dot, y - 4.5);
            canvas.Children.Add(dot);
        }

        _gesture = new TimeGridGesture(geometry, items);
        _geometry = geometry;
    }

    private GridGeometry? _geometry;

    // An event: a solid block in its color, title and time inside. Dashed when it's still a draft.
    private FrameworkElement EventBlock(string title, string color, int start, int end, int day, bool draft, bool selected)
    {
        var lines = new StackPanel { Margin = new Thickness(6, 2, 4, 2) };
        lines.Children.Add(new TextBlock { Text = title.Length == 0 ? "(No title)" : title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        if (end - start >= 40)
            lines.Children.Add(new TextBlock { Text = $"{Clock(_gridDays[day], start)} – {Clock(_gridDays[day], end)}", FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis });
        var block = new Border
        {
            CornerRadius = new CornerRadius(5), Background = HexBrush(color, draft ? 0.7 : 0.95), Child = lines, ClipToBounds = true,
            BorderThickness = new Thickness(selected ? 2 : 0), BorderBrush = Brushes.White
        };
        return block;
    }

    // Time you tracked: an outline in the accent color, so it reads as "what I did".
    private FrameworkElement SessionBlock(ActivityEntry entry, int start, int end)
    {
        var grid = new Grid();
        var fill = new Rectangle { RadiusX = 5, RadiusY = 5, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 3, 2 } };
        Themed(fill, Shape.StrokeProperty, "AccentBrush");
        Themed(fill, Shape.FillProperty, "CardBrush");
        fill.Opacity = 0.9;
        grid.Children.Add(fill);
        var lines = new StackPanel { Margin = new Thickness(6, 2, 4, 2) };
        var title = new TextBlock { Text = "▶ " + entry.Title, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        Themed(title, TextBlock.ForegroundProperty, "AccentBrush");
        lines.Children.Add(title);
        if (end - start >= 40)
        {
            var sub = new TextBlock { Text = $"{ActivityCalendar.Duration(entry.Seconds)} tracked" + (string.IsNullOrEmpty(entry.Detail) ? "" : " · " + entry.Detail), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
            Themed(sub, TextBlock.ForegroundProperty, "MutedTextBrush");
            lines.Children.Add(sub);
        }
        grid.Children.Add(lines);
        return grid;
    }

    private FrameworkElement TaskBlock(ActivityEntry entry)
    {
        var label = new TextBlock
        {
            Text = Glyph(entry.Kind) + " " + entry.Title, FontSize = 11, Margin = new Thickness(5, 1, 4, 1), TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = entry.Kind == ActivityKind.Done ? TextDecorations.Strikethrough : null
        };
        Themed(label, TextBlock.ForegroundProperty, entry.Overdue ? "DangerBrush" : entry.Kind == ActivityKind.Done ? "MutedTextBrush" : "TextBrush");
        var pill = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = label, ClipToBounds = true };
        Themed(pill, Border.BackgroundProperty, "CardHoverBrush");
        Themed(pill, Border.BorderBrushProperty, "BorderBrush");
        return pill;
    }

    // ---------- Mouse on the grid ----------

    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_gesture == null || _surface == null) return;
        var point = e.GetPosition(_surface);
        _gesture.Begin(point.X, point.Y);
        _surface.CaptureMouse();
        e.Handled = true;
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (_gesture == null || _surface == null || _geometry == null) return;
        var point = e.GetPosition(_surface);
        if (!_gesture.Active)
        {
            _surface.Cursor = _gesture.HitTest(point.X, point.Y, out _) switch
            {
                GridTarget.ResizeHandle => Cursors.SizeNS,
                GridTarget.Body => Cursors.Hand,
                _ => Cursors.Arrow
            };
            return;
        }
        if (_gesture.Move(point.X, point.Y) is { } state) ShowGhost(state);
    }

    private void Surface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_gesture == null || _surface == null || !_gesture.Active) return;
        var point = e.GetPosition(_surface);
        var result = _gesture.End(point.X, point.Y);
        _surface.ReleaseMouseCapture();
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
            _ghostText = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(6, 2, 4, 2), TextTrimming = TextTrimming.CharacterEllipsis };
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
        var day = _calMode is CalendarMode.Day or CalendarMode.Week ? _calDay.Date : _calDay.Date;
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
        Dispatcher.BeginInvoke(() => { _evTitle?.Focus(); _evTitle?.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
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

    private Border BuildEventEditor()
    {
        var ev = _editEvent!;
        var panel = new StackPanel();

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = new Button { Content = "✕", Padding = new Thickness(8, 2, 8, 2), Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.SetValue(DockPanel.DockProperty, Dock.Right);
        System.Windows.Automation.AutomationProperties.SetName(close, "Close event editor");
        close.Click += (_, _) => CloseEventEditor();
        head.Children.Add(close);
        var heading = new TextBlock { Text = _editIsNew ? "New event" : "Edit event", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Themed(heading, TextBlock.ForegroundProperty, "TextBrush");
        head.Children.Add(heading);
        panel.Children.Add(head);

        _evTitle = new TextBox { Text = ev.Title, FontSize = 15, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_evTitle, "EventTitleBox");
        System.Windows.Automation.AutomationProperties.SetName(_evTitle, "Event title");
        _evTitle.TextChanged += (_, _) => LiveUpdateEditor();
        _evTitle.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SaveEvent(); e.Handled = true; } else if (e.Key == Key.Escape) { CloseEventEditor(); e.Handled = true; } };
        panel.Children.Add(_evTitle);

        // Date: ‹ Thursday, Oct 8 ›
        var dateRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var next = new Button { Content = "›", Padding = new Thickness(8, 2, 8, 2), Cursor = Cursors.Hand };
        next.SetValue(DockPanel.DockProperty, Dock.Right);
        System.Windows.Automation.AutomationProperties.SetName(next, "Next day for event");
        var prev = new Button { Content = "‹", Padding = new Thickness(8, 2, 8, 2), Cursor = Cursors.Hand };
        prev.SetValue(DockPanel.DockProperty, Dock.Left);
        System.Windows.Automation.AutomationProperties.SetName(prev, "Previous day for event");
        _evDateText = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_evDateText, "EventDateText");
        Themed(_evDateText, TextBlock.ForegroundProperty, "TextBrush");
        UpdateEventDateText();
        prev.Click += (_, _) => { _evDate = _evDate.AddDays(-1); UpdateEventDateText(); LiveUpdateEditor(); };
        next.Click += (_, _) => { _evDate = _evDate.AddDays(1); UpdateEventDateText(); LiveUpdateEditor(); };
        dateRow.Children.Add(prev);
        dateRow.Children.Add(next);
        dateRow.Children.Add(_evDateText);
        panel.Children.Add(dateRow);

        // Start – end
        var times = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _evStart = new TextBox { Text = Clock(ev.Start), Padding = new Thickness(8, 5, 8, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_evStart, "EventStartBox");
        System.Windows.Automation.AutomationProperties.SetName(_evStart, "Event start time");
        _evStart.TextChanged += (_, _) => LiveUpdateEditor();
        var dash = new TextBlock { Text = "–", Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Themed(dash, TextBlock.ForegroundProperty, "MutedTextBrush");
        _evEnd = new TextBox { Text = Clock(ev.End), Padding = new Thickness(8, 5, 8, 5), HorizontalContentAlignment = HorizontalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_evEnd, "EventEndBox");
        System.Windows.Automation.AutomationProperties.SetName(_evEnd, "Event end time");
        _evEnd.TextChanged += (_, _) => LiveUpdateEditor();
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

        var notesLabel = new TextBlock { Text = "NOTES", FontSize = 10, Margin = new Thickness(0, 0, 0, 4) };
        Themed(notesLabel, TextBlock.ForegroundProperty, "MutedTextBrush");
        panel.Children.Add(notesLabel);
        _evNotes = new TextBox { Text = ev.Notes ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, MaxHeight = 120, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 10) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_evNotes, "EventNotesBox");
        System.Windows.Automation.AutomationProperties.SetName(_evNotes, "Event notes");
        panel.Children.Add(_evNotes);

        _evError = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        Themed(_evError, TextBlock.ForegroundProperty, "DangerBrush");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_evError, "EventErrorText");
        panel.Children.Add(_evError);

        var buttons = new DockPanel { LastChildFill = false };
        var save = new Button { Content = "Save", MinWidth = 84, Padding = new Thickness(12, 6, 12, 6), Style = (Style)FindResource("AccentButton") };
        save.SetValue(DockPanel.DockProperty, Dock.Right);
        System.Windows.Automation.AutomationProperties.SetName(save, "Save event");
        save.Click += (_, _) => SaveEvent();
        buttons.Children.Add(save);
        if (!_editIsNew)
        {
            var delete = new Button { Content = "Delete", Padding = new Thickness(12, 6, 12, 6) };
            System.Windows.Automation.AutomationProperties.SetName(delete, "Delete event");
            delete.Click += (_, _) => DeleteEvent();
            buttons.Children.Add(delete);
        }
        panel.Children.Add(buttons);

        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16), Width = 300, VerticalAlignment = VerticalAlignment.Top, Child = panel };
        Themed(card, Border.BackgroundProperty, "CardBrush");
        Themed(card, Border.BorderBrushProperty, "SoftBorderBrush");
        _eventEditorPanel = card;
        return card;
    }

    // The block on the grid follows what's typed, so you see the event take shape before saving.
    private void LiveUpdateEditor()
    {
        if (_editEvent == null || _evTitle == null || _evStart == null || _evEnd == null) return;
        _editEvent.Title = _evTitle.Text;
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
            var dot = new Ellipse { Width = 16, Height = 16, Fill = HexBrush(color) };
            var ring = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
                BorderBrush = selected ? Brushes.White : Brushes.Transparent, Background = Brushes.Transparent, Child = dot
            };
            var button = new Button
            {
                Content = ring, Cursor = Cursors.Hand, Focusable = false, Margin = new Thickness(0, 0, 3, 0),
                Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
                ToolTip = EventPalette.Name(color)
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"Event color {EventPalette.Name(color)}");
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
            _evError.Visibility = Visibility.Visible;
            return;
        }

        var ev = _editEvent;
        ev.Title = _evTitle.Text.Trim();
        ev.Start = _evDate.Date.Add(start!.Value.ToTimeSpan());
        ev.End = end!.Value == TimeOnly.MinValue ? _evDate.Date.AddDays(1) : _evDate.Date.Add(end.Value.ToTimeSpan()); // 12am as an end means midnight
        ev.Color = _evColor;
        ev.Notes = string.IsNullOrWhiteSpace(_evNotes?.Text) ? null : _evNotes!.Text.Trim();

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

        const double rowHeight = 38;
        const double sectionHeight = 34;
        var height = 12.0;
        foreach (var section in sections) height += sectionHeight + section.Rows.Count * rowHeight + 10;
        height = Math.Max(height, 200);
        var width = days * TimelineDayWidth;

        // The ruler: month names, then each day (today in a red circle).
        var ruler = new Canvas { Width = width, Height = 54, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var monthLabels = new List<(TextBlock Label, double X)>();
        for (var i = 0; i < days; i++)
        {
            var day = from.AddDays(i);
            if (i == 0 || day.Day == 1)
            {
                var month = new TextBlock { Text = day.ToString(i == 0 || day.Month == 1 ? "MMMM yyyy" : "MMMM", CultureInfo.CurrentCulture), FontSize = 13, FontWeight = FontWeights.SemiBold };
                Themed(month, TextBlock.ForegroundProperty, "TextBrush");
                Canvas.SetLeft(month, i * TimelineDayWidth + 6);
                ruler.Children.Add(month);
                monthLabels.Add((month, i * TimelineDayWidth + 6));
            }
            var isToday = day == today;
            var number = new TextBlock { Text = day.Day.ToString(), FontSize = 12, FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal, Foreground = isToday ? Brushes.White : null, TextAlignment = TextAlignment.Center, Width = 24 };
            if (!isToday) Themed(number, TextBlock.ForegroundProperty, day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "FaintTextBrush" : "MutedTextBrush");
            var holder = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Child = number, Padding = new Thickness(0, 3, 0, 0) };
            if (isToday) holder.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
            Canvas.SetLeft(holder, i * TimelineDayWidth + (TimelineDayWidth - 24) / 2);
            Canvas.SetTop(holder, 26);
            ruler.Children.Add(holder);
        }
        // The month of the day at the left edge, kept in view; hidden while a month's own label is there.
        var sticky = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold };
        Themed(sticky, TextBlock.ForegroundProperty, "TextBrush");
        ruler.Children.Add(sticky);
        var rulerScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = ruler };

        // The body.
        var canvas = new Canvas { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        for (var i = 0; i < days; i++)
        {
            var day = from.AddDays(i);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                var band = new Rectangle { Width = TimelineDayWidth, Height = height, Opacity = 0.035 };
                Themed(band, Shape.FillProperty, "TextBrush");
                Canvas.SetLeft(band, i * TimelineDayWidth);
                canvas.Children.Add(band);
            }
        }
        var headings = new List<TextBlock>();
        var y = 10.0;
        foreach (var section in sections)
        {
            var title = new TextBlock { Text = $"{section.Title}  {section.Rows.Count}", FontSize = 12, FontWeight = FontWeights.SemiBold };
            Themed(title, TextBlock.ForegroundProperty, "MutedTextBrush");
            Canvas.SetTop(title, y + 8);
            Canvas.SetLeft(title, 8);
            canvas.Children.Add(title);
            headings.Add(title);
            y += sectionHeight;
            foreach (var row in section.Rows)
            {
                foreach (var bar in row.Bars) AddTimelineBar(canvas, bar, from, y, rowHeight, width, bar == row.Bars[^1]);
                y += rowHeight;
            }
            y += 10;
        }
        var todayX = (today - from).Days * TimelineDayWidth + TimelineDayWidth / 2;
        var todayLine = new Rectangle { Width = 2, Height = height, IsHitTestVisible = false };
        todayLine.Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
        Canvas.SetLeft(todayLine, todayX - 1);
        canvas.Children.Add(todayLine);
        if (sections.Count == 0)
        {
            var empty = new TextBlock { Text = "Nothing on the timeline yet. Add an event or give a task a date.", FontSize = 13 };
            Themed(empty, TextBlock.ForegroundProperty, "MutedTextBrush");
            Canvas.SetLeft(empty, todayX + 20);
            Canvas.SetTop(empty, 20);
            canvas.Children.Add(empty);
        }

        var bodyScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = canvas };
        bodyScroll.ScrollChanged += (_, e) =>
        {
            var offset = bodyScroll.HorizontalOffset;
            rulerScroll.ScrollToHorizontalOffset(offset);
            foreach (var heading in headings) Canvas.SetLeft(heading, offset + 8); // section titles stay in view
            var leftDay = from.AddDays((int)Math.Min(days - 1, offset / TimelineDayWidth));
            sticky.Text = leftDay.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            Canvas.SetLeft(sticky, offset + 6);
            sticky.Visibility = monthLabels.Any(m => m.X >= offset - 4 && m.X < offset + 170) ? Visibility.Collapsed : Visibility.Visible;
        };
        bodyScroll.Loaded += (_, _) => bodyScroll.ScrollToHorizontalOffset(Math.Max(0, todayX - 220));
        _timelineScroll = bodyScroll;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(rulerScroll);
        Grid.SetRow(bodyScroll, 1);
        layout.Children.Add(bodyScroll);
        CalendarBody.Children.Add(layout);
    }

    private void AddTimelineBar(Canvas canvas, TimelineBar bar, DateTime from, double y, double rowHeight, double canvasWidth, bool last)
    {
        var x = Math.Max(0, (bar.Start - from).Days) * TimelineDayWidth;
        var right = ((bar.End - from).Days + 1) * TimelineDayWidth;
        var barWidth = Math.Max(TimelineDayWidth - 4, Math.Min(right, canvasWidth) - x - 4);
        var barHeight = rowHeight - 10;

        var fill = HexBrush(bar.Color, bar.Done ? 0.35 : 0.9);
        var pill = new Border { Width = barWidth, Height = barHeight, CornerRadius = new CornerRadius(7), Background = fill };
        if (fill == null)
        {
            Themed(pill, Border.BackgroundProperty, bar.Overdue ? "DangerBrush" : bar.Kind == TimelineKind.Work ? "CardHoverBrush" : "AccentBrush");
            pill.Opacity = bar.Done ? 0.4 : 0.9;
        }
        if (bar.Overdue && fill != null) pill.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
        if (bar.Overdue && fill != null) pill.BorderThickness = new Thickness(2);

        var inside = bar.Kind == TimelineKind.Work ? bar.Detail ?? "" : barWidth >= 140 ? bar.Label : "";
        if (inside.Length > 0)
        {
            var work = bar.Kind == TimelineKind.Work;
            var text = new TextBlock
            {
                Text = inside, FontSize = work ? 11 : 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Margin = work ? new Thickness(1, 0, 1, 0) : new Thickness(8, 0, 6, 0), TextAlignment = work ? TextAlignment.Center : TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = fill != null ? Brushes.White : null
            };
            if (fill == null) Themed(text, TextBlock.ForegroundProperty, bar.Kind == TimelineKind.Work ? "TextBrush" : "BgBrush");
            else if (bar.Done) Themed(text, TextBlock.ForegroundProperty, "MutedTextBrush");
            if (bar.Done) text.TextDecorations = TextDecorations.Strikethrough;
            pill.Child = text;
        }
        Canvas.SetLeft(pill, x + 2);
        Canvas.SetTop(pill, y + 5);
        canvas.Children.Add(pill);

        // The name beside the bar when it doesn't fit inside (and, for work, after the last day).
        var outside = bar.Kind == TimelineKind.Work ? (last ? bar.Label : "") : inside.Length == 0 ? bar.Label : "";
        if (outside.Length > 0)
        {
            var name = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            name.Inlines.Add(new System.Windows.Documents.Run(outside) { FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(bar.Detail) && bar.Kind != TimelineKind.Work)
                name.Inlines.Add(new System.Windows.Documents.Run("   " + bar.Detail) { FontSize = 11 });
            Themed(name, TextBlock.ForegroundProperty, bar.Done ? "MutedTextBrush" : "TextBrush");
            if (bar.Done) name.TextDecorations = TextDecorations.Strikethrough;
            Canvas.SetLeft(name, x + barWidth + 10);
            Canvas.SetTop(name, y + rowHeight / 2 - 9);
            canvas.Children.Add(name);
        }

        if (bar.Kind == TimelineKind.Event && bar.SourceId != null)
        {
            var id = bar.SourceId;
            pill.Cursor = Cursors.Hand;
            pill.MouseLeftButtonUp += (_, _) => { if (_events.FirstOrDefault(e => e.Id == id) is { } ev) OpenEventEditor(ev.Clone(), false); };
        }
    }
}

internal static class CalendarLet
{
    public static void Let<T>(this T value, Action<T> action) { if (value != null) action(value); }
}
