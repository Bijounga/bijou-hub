using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Services.GoogleTasks;

namespace BijouHub.Mac;

// The pop-out task list: today's open tasks in a small always-on-top window, with the same section
// tabs as Home, and a switch to a calendar view (a day agenda with a week strip). Same behavior as Windows.
public partial class MainWindow
{
    private TasksPopoutWindow? _tasksPopout;
    private string _popoutScope = EditingGroup;
    private bool _popoutCalendar;
    private DateTime _popoutDay = DateTime.Today;

    private void TasksPopoutToggle_Click(object? sender, RoutedEventArgs e) => ToggleTasksPopout();

    private void ToggleTasksPopout()
    {
        if (_tasksPopout != null)
        {
            _tasksPopout.Close();
            return;
        }

        var window = new TasksPopoutWindow();
        window.AddRequested += text =>
        {
            LoadDailyPlan(); // past midnight, it belongs to the new day
            var group = !GoogleMode || _popoutScope == AllScope ? EditingGroup : _popoutScope;
            AddDailyGoal(new DailyGoal { Text = text, Group = group });
            UpdateDailyProgress();
            RefreshTasksPopout();
        };
        window.OpenCalendarRequested += () =>
        {
            BringToFront();
            ShowCalendar(_popoutDay, CalendarMode.Day);
        };
        window.Closed += (_, _) =>
        {
            _tasksPopout = null;
            ToolTip.SetTip(TasksPopoutButton, "Pop out the task list");
        };

        _tasksPopout = window;
        _popoutDay = DateTime.Today;
        ToolTip.SetTip(TasksPopoutButton, "Close the pop-out task list");
        RefreshTasksPopout();
        window.Show();
    }

    private bool PopoutInScope(DailyGoal goal) =>
        !GoogleMode || _popoutScope == AllScope || GoogleGoalsSync.GroupOf(goal) == _popoutScope;

    // Redraws the pop-out: header switch, tabs / day picker, and the task rows or the day agenda.
    private void RefreshTasksPopout()
    {
        if (_tasksPopout is not { } window) return;
        var groups = ScopeGroups();
        if (_popoutScope != AllScope && !groups.Contains(_popoutScope)) _popoutScope = EditingGroup;

        window.HeaderPills.Children.Clear();
        var tasksPill = CalPill("Tasks", !_popoutCalendar, () => SetPopoutMode(false), "Popout tasks view");
        tasksPill.Margin = new Thickness(0);
        window.HeaderPills.Children.Add(tasksPill);
        window.HeaderPills.Children.Add(CalPill("Calendar", _popoutCalendar, () => SetPopoutMode(true), "Popout calendar view"));
        window.ShowCalendarMode(_popoutCalendar);

        window.TopBar.Children.Clear();
        if (_popoutCalendar)
        {
            BuildPopoutCalendar(window);
            return;
        }

        BuildPopoutTabs(window, groups);
        window.TasksHost.Children.Clear();
        var rows = _dailyGoals.Where(g => !g.Done && !g.IsPlannedAfter(TodayKey) && PopoutInScope(g)).ToList();
        foreach (var goal in rows) window.TasksHost.Children.Add(PopoutTaskRow(goal));
        window.SetEmpty(rows.Count == 0);
    }

    private Control PopoutTaskRow(DailyGoal goal)
    {
        var box = new CheckBox { IsChecked = false, MinWidth = 0, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0) };
        Avalonia.Automation.AutomationProperties.SetName(box, goal.Text);
        box.Click += (_, _) => goal.Done = true; // it drops off the open list when the list is redrawn

        var text = new StackPanel { Children = { new TextBlock { Text = goal.Text, FontSize = 13, TextWrapping = TextWrapping.Wrap } } };
        if (!string.IsNullOrEmpty(goal.DueChip))
            text.Children.Add(new TextBlock { Text = goal.DueChip, FontSize = 11, Foreground = Brush("MutedTextBrush") });

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetColumn(text, 1);
        row.Children.Add(box);
        row.Children.Add(text);
        if (!string.IsNullOrEmpty(goal.ChipText))
        {
            var chip = new TextBlock { Text = goal.ChipText, FontSize = 11, MaxWidth = 90, Margin = new Thickness(8, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("AccentBrush") };
            Grid.SetColumn(chip, 2);
            row.Children.Add(chip);
        }
        return row;
    }

    private void SetPopoutMode(bool calendar)
    {
        _popoutCalendar = calendar;
        if (calendar) _popoutDay = DateTime.Today;
        RefreshTasksPopout();
    }

    // The Home tabs: All first, then Editing / Life / Study / Other. With no Google connection there's one list, so no tabs.
    private void BuildPopoutTabs(TasksPopoutWindow window, IReadOnlyList<string> groups)
    {
        if (!GoogleMode) return;
        var tabs = new WrapPanel();
        foreach (var scope in groups.Prepend(AllScope))
        {
            var count = _dailyGoals.Count(g => !g.Done && !g.IsPlannedAfter(TodayKey) && (scope == AllScope || GoogleGoalsSync.GroupOf(g) == scope));
            var s = scope;
            var pill = CalPill($"{GroupLabel(scope)}  {count}", _popoutScope == scope, () =>
            {
                _popoutScope = s;
                RefreshTasksPopout();
            }, $"Popout {GroupLabel(scope)} tab");
            pill.Margin = new Thickness(0, 0, 6, 6);
            tabs.Children.Add(pill);
        }
        window.TopBar.Children.Add(tabs);
    }

    // ‹ Thursday, Oct 8 › Today, a strip of the week, and that day's agenda below.
    private void BuildPopoutCalendar(TasksPopoutWindow window)
    {
        var next = CalPill("›", false, () => ShiftPopoutDay(1), "Popout next day");
        var today = CalPill("Today", false, () => { _popoutDay = DateTime.Today; RefreshTasksPopout(); }, "Popout today");
        var prev = CalPill("‹", false, () => ShiftPopoutDay(-1), "Popout previous day");
        prev.Margin = new Thickness(0);
        var title = new TextBlock
        {
            Text = DayHeading(_popoutDay), FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        };
        var nav = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(next, Dock.Right);
        DockPanel.SetDock(today, Dock.Right);
        DockPanel.SetDock(prev, Dock.Left);
        nav.Children.Add(next);
        nav.Children.Add(today);
        nav.Children.Add(prev);
        nav.Children.Add(title);
        window.TopBar.Children.Add(nav);

        // The week strip: a button per day, with a dot when something's on it.
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var weekStart = _popoutDay.Date.AddDays(-(((int)_popoutDay.DayOfWeek - (int)first + 7) % 7));
        _events = _eventStore.Load();
        var counts = CalendarEntries(weekStart, weekStart.AddDays(6)).GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.Count());
        var strip = new UniformGrid { Rows = 1 };
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            var selected = day == _popoutDay.Date;
            var isToday = day == DateTime.Today;
            var abbreviation = day.ToString("ddd", CultureInfo.CurrentCulture);
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = abbreviation.Length > 3 ? abbreviation[..3] : abbreviation, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush(selected ? "AccentBrush" : "MutedTextBrush") },
                    new TextBlock { Text = day.Day.ToString(), FontSize = 14, FontWeight = isToday || selected ? FontWeight.SemiBold : FontWeight.Normal, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush(selected || isToday ? "AccentBrush" : "TextBrush") },
                    new Ellipse { Width = 4, Height = 4, Margin = new Thickness(0, 2, 0, 0), Fill = Brush("AccentBrush"), Opacity = counts.ContainsKey(day) ? 1 : 0 }
                }
            };
            var cell = new Border
            {
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(2, 4, 2, 3), Margin = new Thickness(1, 0), Child = stack,
                Background = Brush(selected ? "CardHoverBrush" : "CardBrush"), BorderBrush = Brush(selected ? "AccentBrush" : "SoftBorderBrush")
            };
            var button = new Button { Content = cell, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            Avalonia.Automation.AutomationProperties.SetName(button, $"Popout day {day.ToString("MMM d", CultureInfo.InvariantCulture)}");
            var d = day;
            button.Click += (_, _) => { _popoutDay = d; RefreshTasksPopout(); };
            strip.Children.Add(button);
        }
        window.TopBar.Children.Add(strip);

        // That day's agenda: events, timed tasks, finished tasks and tracked work.
        window.AgendaHost.Children.Clear();
        var dayEntries = CalendarEntries(_popoutDay.Date, _popoutDay.Date);
        if (dayEntries.Count == 0)
            window.AgendaHost.Children.Add(new TextBlock { Text = "Nothing on this day.", FontSize = 12, Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("MutedTextBrush") });
        foreach (var entry in dayEntries) window.AgendaHost.Children.Add(BuildEntryRow(entry, interactive: false));
    }

    private void ShiftPopoutDay(int days)
    {
        _popoutDay = _popoutDay.AddDays(days);
        RefreshTasksPopout();
    }
}
