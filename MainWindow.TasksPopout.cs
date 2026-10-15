using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Services.GoogleTasks;
using BijouHub.Views;

namespace BijouHub;

// The pop-out task list: today's open tasks in a small always-on-top window, with the same section
// tabs as Home, and a switch to a calendar view (a day agenda with a week strip).
public partial class MainWindow
{
    private TasksPopoutWindow? _tasksPopout;
    private ListCollectionView? _popoutView;
    private string _popoutScope = EditingGroup;
    private bool _popoutCalendar;
    private DateTime _popoutDay = DateTime.Today;

    private void TasksPopoutToggle_Click(object sender, RoutedEventArgs e) => ToggleTasksPopout();

    private void ToggleTasksPopout()
    {
        if (_tasksPopout != null)
        {
            _tasksPopout.Close();
            return;
        }

        var window = new TasksPopoutWindow();
        WidgetPlacement.Attach(window, _settingsStore.Load().TaskWidget, true, place =>
        {
            var settings = _settingsStore.Load();
            settings.TaskWidget.X = place.X;
            settings.TaskWidget.Y = place.Y;
            settings.TaskWidget.Width = place.Width;
            settings.TaskWidget.Height = place.Height;
            _settingsStore.Save(settings);
        });
        _popoutView = new ListCollectionView(_dailyGoals)
        {
            Filter = item => item is DailyGoal g && !g.Done && !g.IsPlannedAfter(TodayKey) && PopoutInScope(g),
            IsLiveFiltering = true
        };
        foreach (var property in new[] { nameof(DailyGoal.Done), nameof(DailyGoal.Group), nameof(DailyGoal.ProjectName), nameof(DailyGoal.Due) })
            _popoutView.LiveFilteringProperties.Add(property);
        window.TasksList.ItemsSource = _popoutView;

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
            _popoutView = null;
            SetIconButton(TasksPopoutButton, "", "Pop out the task list");
        };

        _tasksPopout = window;
        _popoutDay = DateTime.Today;
        SetIconButton(TasksPopoutButton, "", "Close the pop-out task list");
        RefreshTasksPopout();
        window.Show();
    }

    private bool PopoutInScope(DailyGoal goal) =>
        !GoogleMode || _popoutScope == AllScope || GoogleGoalsSync.GroupOf(goal) == _popoutScope;

    // Redraws the pop-out's tabs / day picker and its calendar agenda. The task list itself follows
    // the goals live; this keeps the counts on the tabs right.
    private void RefreshTasksPopout()
    {
        if (_tasksPopout is not { } window) return;
        var groups = ScopeGroups();
        if (_popoutScope != AllScope && !groups.Contains(_popoutScope)) _popoutScope = EditingGroup;

        window.HeaderPills.Children.Clear();
        window.HeaderPills.Children.Add(CalPill("Tasks", !_popoutCalendar, () => SetPopoutMode(false), "Popout tasks view"));
        window.HeaderPills.Children.Add(CalPill("Calendar", _popoutCalendar, () => SetPopoutMode(true), "Popout calendar view"));
        window.HeaderPills.Children[0].SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        window.ShowCalendarMode(_popoutCalendar);

        window.TopBar.Children.Clear();
        if (_popoutCalendar) BuildPopoutCalendar(window);
        else BuildPopoutTabs(window, groups);
        _popoutView?.Refresh();
        window.SetEmpty(!_popoutCalendar && (_popoutView?.Count ?? 0) == 0);
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
        var nav = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var next = CalPill("›", false, () => ShiftPopoutDay(1), "Popout next day");
        DockPanel.SetDock(next, Dock.Right);
        var today = CalPill("Today", false, () => { _popoutDay = DateTime.Today; RefreshTasksPopout(); }, "Popout today");
        DockPanel.SetDock(today, Dock.Right);
        var prev = CalPill("‹", false, () => ShiftPopoutDay(-1), "Popout previous day");
        prev.Margin = new Thickness(0);
        DockPanel.SetDock(prev, Dock.Left);
        var title = new TextBlock { Text = DayHeading(_popoutDay), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Themed(title, TextBlock.ForegroundProperty, "TextBrush");
        nav.Children.Add(next);
        nav.Children.Add(today);
        nav.Children.Add(prev);
        nav.Children.Add(title);
        window.TopBar.Children.Add(nav);

        // The week strip: a button per day, with a dot when something's on it.
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var weekStart = _popoutDay.Date.AddDays(-(((int)_popoutDay.DayOfWeek - (int)first + 7) % 7));
        _events = _eventStore.Load();
        var entries = CalendarEntries(weekStart, weekStart.AddDays(6)).GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.Count());
        var strip = new UniformGrid { Rows = 1 };
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            var selected = day == _popoutDay.Date;
            var isToday = day == DateTime.Today;
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var name = new TextBlock { Text = day.ToString("ddd", CultureInfo.CurrentCulture).Substring(0, Math.Min(3, day.ToString("ddd", CultureInfo.CurrentCulture).Length)), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
            Themed(name, TextBlock.ForegroundProperty, selected ? "AccentBrush" : "MutedTextBrush");
            var number = new TextBlock { Text = day.Day.ToString(), FontSize = 14, FontWeight = isToday || selected ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center };
            Themed(number, TextBlock.ForegroundProperty, selected || isToday ? "AccentBrush" : "TextBrush");
            var dot = new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, Margin = new Thickness(0, 2, 0, 0), Opacity = entries.ContainsKey(day) ? 1 : 0 };
            Themed(dot, System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
            stack.Children.Add(name);
            stack.Children.Add(number);
            stack.Children.Add(dot);
            var cell = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(2, 4, 2, 3), Margin = new Thickness(1, 0, 1, 0), Child = stack };
            Themed(cell, Border.BackgroundProperty, selected ? "CardHoverBrush" : "CardBrush");
            Themed(cell, Border.BorderBrushProperty, selected ? "AccentBrush" : "SoftBorderBrush");
            var button = new Button
            {
                Content = cell, Cursor = System.Windows.Input.Cursors.Hand, Focusable = false,
                Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"Popout day {day.ToString("MMM d", CultureInfo.InvariantCulture)}");
            var d = day;
            button.Click += (_, _) => { _popoutDay = d; RefreshTasksPopout(); };
            strip.Children.Add(button);
        }
        window.TopBar.Children.Add(strip);

        // That day's agenda: events, timed tasks, finished tasks and tracked work.
        window.AgendaHost.Children.Clear();
        var dayEntries = CalendarEntries(_popoutDay.Date, _popoutDay.Date);
        if (dayEntries.Count == 0)
        {
            var none = new TextBlock { Text = "Nothing on this day.", FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
            Themed(none, TextBlock.ForegroundProperty, "MutedTextBrush");
            window.AgendaHost.Children.Add(none);
        }
        foreach (var entry in dayEntries) window.AgendaHost.Children.Add(BuildEntryRow(entry, interactive: false));
    }

    private void ShiftPopoutDay(int days)
    {
        _popoutDay = _popoutDay.AddDays(days);
        RefreshTasksPopout();
    }
}
