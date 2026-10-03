using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Dates and times on goals: the calendar icon (on the add bar and each goal) opens the picker,
// goals show "Tomorrow 3:00 PM"-style chips (red once past due), and a timed goal pops a
// reminder when its time comes. Same behavior as Windows.
public partial class MainWindow
{
    private DailyGoal? _dueTarget;
    private DateTime? _pendingDue;
    private TimeOnly? _pendingTime;
    private DuePicker? _duePicker;
    private Flyout? _dueFlyout;
    private DispatcherTimer? _dueTimer;
    private DateTime _lastReminderCheck;
    private readonly List<ReminderWindow> _reminders = new();

    private void InitDue()
    {
        _duePicker = new DuePicker();
        _duePicker.Picked += DuePicker_Picked;
        _dueFlyout = new Flyout { Content = _duePicker, Placement = PlacementMode.BottomEdgeAlignedLeft };
        _dueFlyout.FlyoutPresenterClasses.Add("bare");
        _dueFlyout.Closed += (_, _) =>
        {
            if (_dueTarget == null) (TasksPanel.IsVisible ? TasksInput : DailyGoalInput).Focus();
            _dueTarget = null;
            UpdateAddGoalBar();
        };

        _lastReminderCheck = DateTime.Now;
        _dueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _dueTimer.Tick += (_, _) => CheckReminders();
        _dueTimer.Start();
    }

    private bool DuePickerOpenForBar => _dueFlyout?.IsOpen == true && _dueTarget == null;

    private void OpenDuePicker(Control anchor, DailyGoal? goal)
    {
        _dueTarget = goal;
        _duePicker!.Load(goal != null ? DueText.Day(goal.Due) : _pendingDue, goal != null ? DueText.Time(goal.DueTime) : _pendingTime);
        _dueFlyout!.ShowAt(anchor);
    }

    private void DuePicker_Picked(DateTime? date, TimeOnly? time)
    {
        if (_dueTarget is not { } goal)
        {
            _pendingDue = date;
            _pendingTime = time;
            UpdateAddGoalBar();
            return;
        }

        goal.DueTime = time?.ToString("HH:mm", CultureInfo.InvariantCulture); // kept locally
        goal.Due = date is DateTime d ? DueText.Key(d) : null; // saved and synced
        SyncVisibleGoals();
        RefreshDueChips();
        UpdateDailyProgress();
    }

    private void AddGoalDue_Click(object? sender, RoutedEventArgs e) => OpenDuePicker(AddGoalDueButton, null);

    private void GoalDue_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DailyGoal goal } control) OpenDuePicker(control, goal);
    }

    private void GoalDueChip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: DailyGoal goal } control) OpenDuePicker(control, goal);
        e.Handled = true;
    }

    private void UpdateAddGoalDue(bool barActive)
    {
        var picked = _pendingDue != null || _pendingTime != null;
        AddGoalDueText.Text = picked
            ? string.Join(" ", new[]
            {
                _pendingDue is DateTime d ? DueText.DayLabel(d, DateTime.Today) : null,
                _pendingTime is TimeOnly t ? DueText.Time12(t) : null
            }.Where(s => s != null))
            : "";
        AddGoalDueText.IsVisible = picked;
        TasksDueText.Text = AddGoalDueText.Text; // the Tasks page's add bar shares the pick
        TasksDueText.IsVisible = picked;
        AddGoalDueButton.IsVisible = barActive || picked || DuePickerOpenForBar;
        ToolTip.SetTip(AddGoalDueButton, picked ? "Change the date and time" : "Add a date and time");
    }

    private (string? Due, string? Time) NewGoalDue() =>
        (_pendingDue is DateTime d ? DueText.Key(d) : DayViewDefault(), _pendingTime?.ToString("HH:mm", CultureInfo.InvariantCulture));

    // The picked date/time applies to what was just added, then the bar starts fresh.
    private void ClearPendingDue()
    {
        _pendingDue = null;
        _pendingTime = null;
        UpdateAddGoalBar();
    }

    private DateTime ViewedDay => _dayView switch
    {
        DayView.Today => DateTime.Today,
        DayView.Tomorrow => DateTime.Today.AddDays(1),
        _ => DateTime.MinValue
    };

    private void RefreshDueChips()
    {
        var now = DateTime.Now;
        var viewed = ViewedDay;
        foreach (var goal in _dailyGoals)
        {
            var (text, overdue) = DueText.Chip(goal, viewed, now);
            goal.DueChip = text;
            goal.DueOverdue = overdue;
        }
    }

    // ---------- Reminders ----------

    private void CheckReminders()
    {
        var now = DateTime.Now;
        foreach (var goal in _dailyGoals.Where(g => !g.Done && g.DueAt is DateTime at && at > _lastReminderCheck && at <= now).ToList())
            ShowReminder(goal);
        if (now.Minute != _lastReminderCheck.Minute) RefreshDueChips();
        _lastReminderCheck = now;
    }

    private void ShowReminder(DailyGoal goal)
    {
        Chime("Glass");
        var when = DueText.Time(goal.DueTime) is TimeOnly time ? DueText.Time12(time) : "Now";
        var card = new ReminderWindow(goal.Text, $"Reminder · {when}");
        card.DoneRequested += () => goal.Done = true;
        card.SnoozeRequested += () =>
        {
            var snoozed = DateTime.Now.AddMinutes(10);
            goal.DueTime = snoozed.ToString("HH:mm", CultureInfo.InvariantCulture);
            goal.Due = DueText.Key(snoozed.Date);
            RefreshDueChips();
        };
        card.Closed += (_, _) =>
        {
            _reminders.Remove(card);
            StackReminders();
        };
        card.Opened += (_, _) => StackReminders();
        _reminders.Add(card);
        card.Show();
    }

    // Several at once stack up from the corner instead of covering each other.
    private void StackReminders()
    {
        if (Screens.Primary is not { } screen) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var bottom = area.Bottom - (int)(16 * scale);
        foreach (var card in _reminders)
        {
            var height = (int)((card.Bounds.Height > 0 ? card.Bounds.Height : 130) * scale);
            var width = (int)(card.Width * scale);
            card.Position = new PixelPoint(area.Right - width - (int)(16 * scale), bottom - height);
            bottom -= height + (int)(8 * scale);
        }
    }
}
