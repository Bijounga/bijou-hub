using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

// Dates and times on goals: the calendar icon (on the add bar and each goal) opens the picker,
// goals show "Tomorrow 3:00 PM"-style chips (red once past due), and a timed goal pops a
// reminder when its time comes.
public partial class MainWindow
{
    // The goal the picker is editing, or null for the add bar (its choice applies to the next
    // goal added).
    private DailyGoal? _dueTarget;
    private DateTime? _pendingDue;
    private TimeOnly? _pendingTime;
    private DispatcherTimer? _dueTimer;
    private DateTime _lastReminderCheck;
    private readonly List<ReminderWindow> _reminders = new();

    private void InitDue()
    {
        DuePickerControl.Picked += DuePicker_Picked;
        DuePopup.Closed += (_, _) =>
        {
            if (_dueTarget == null) (TasksPanel.Visibility == Visibility.Visible ? TasksInput : DailyGoalInput).Focus();
            _dueTarget = null;
            UpdateAddGoalBar();
        };

        // Reminders, and chips turning red as times pass. Twice a minute is plenty.
        _lastReminderCheck = DateTime.Now;
        _dueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _dueTimer.Tick += (_, _) => CheckReminders();
        _dueTimer.Start();
    }

    private void OpenDuePicker(UIElement anchor, DailyGoal? goal)
    {
        _dueTarget = goal;
        DuePickerControl.Load(goal != null ? DueText.Day(goal.Due) : _pendingDue, goal != null ? DueText.Time(goal.DueTime) : _pendingTime);
        DuePopup.PlacementTarget = anchor;
        DuePopup.IsOpen = true;
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

        goal.DueTime = time?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture); // kept locally
        goal.Due = date is DateTime d ? DueText.Key(d) : null; // saved and synced
        RefreshGoalViews();
        RefreshDueChips();
        UpdateDailyProgress();
    }

    private void AddGoalDue_Click(object sender, RoutedEventArgs e) => OpenDuePicker(AddGoalDueButton, null);

    private void DailyGoalDue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DailyGoal goal } element) OpenDuePicker(element, goal);
    }

    private void DailyGoalDueChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DailyGoal goal } element) OpenDuePicker(element, goal);
        e.Handled = true;
    }

    // The add bar's calendar: an icon, or the chosen "Tomorrow 3:00 PM" once something's picked.
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
        AddGoalDueText.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;
        TasksDueText.Text = AddGoalDueText.Text; // the Tasks page's add bar shares the pick
        TasksDueText.Visibility = AddGoalDueText.Visibility;
        AddGoalDueButton.Visibility = barActive || picked || (DuePopup.IsOpen && _dueTarget == null) ? Visibility.Visible : Visibility.Collapsed;
        AddGoalDueButton.ToolTip = picked ? "Change the date and time" : "Add a date and time";
    }

    // What a newly added goal gets: the picked date/time, else the day being looked at.
    private (string? Due, string? Time) NewGoalDue()
    {
        var due = _pendingDue is DateTime d ? DueText.Key(d) : DayViewDefault();
        var time = _pendingTime?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return (due, time);
    }

    // The picked date/time applies to what was just added, then the bar starts fresh.
    private void ClearPendingDue()
    {
        _pendingDue = null;
        _pendingTime = null;
        UpdateAddGoalBar();
    }

    // The day a goal's chip leaves out (it's the day being looked at).
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
        SoundFx.Play(SoundFx.Reminder);
        var when = goal.DueTime is { } t && DueText.Time(t) is TimeOnly time ? DueText.Time12(time) : "Now";
        var card = new ReminderWindow(goal.Text, $"Reminder · {when}");
        card.DoneRequested += () => goal.Done = true;
        card.SnoozeRequested += () =>
        {
            var snoozed = DateTime.Now.AddMinutes(10);
            goal.DueTime = snoozed.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            goal.Due = DueText.Key(snoozed.Date);
            RefreshDueChips();
        };
        card.Closed += (_, _) =>
        {
            _reminders.Remove(card);
            StackReminders();
        };
        _reminders.Add(card);
        card.Show();
        StackReminders();
    }

    // Several at once stack up from the corner instead of covering each other.
    private void StackReminders()
    {
        var area = SystemParameters.WorkArea;
        var bottom = area.Bottom - 16;
        foreach (var card in _reminders)
        {
            card.UpdateLayout();
            var height = card.ActualHeight > 0 ? card.ActualHeight : 130;
            card.Left = area.Right - card.Width - 16;
            card.Top = bottom - height;
            bottom -= height + 8;
        }
    }
}
