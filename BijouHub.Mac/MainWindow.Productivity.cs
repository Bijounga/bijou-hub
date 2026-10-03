using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Planning tomorrow, the daily target ring and Quick Capture. Same behavior as Windows.
public partial class MainWindow
{
    // ---------- Plan tomorrow ----------

    // The goal list shows today (plus anything past due; later days hidden), tomorrow's plan, or
    // everything dated after tomorrow.
    private enum DayView { Today, Tomorrow, Upcoming }

    private DayView _dayView = DayView.Today;

    private static string TodayKey => DailyPlanStore.Key(DateTime.Today);
    private static string TomorrowKey => DailyPlanStore.Key(DateTime.Today.AddDays(1));

    private bool InDay(DailyGoal goal) => _dayView switch
    {
        DayView.Tomorrow => goal.Due == TomorrowKey,
        DayView.Upcoming => goal.IsPlannedAfter(TomorrowKey),
        _ => !goal.IsPlannedAfter(TodayKey)
    };

    // A goal added while looking at Tomorrow or Upcoming lands on that day.
    private string? DayViewDefault() => _dayView switch
    {
        DayView.Tomorrow => TomorrowKey,
        DayView.Upcoming => DailyPlanStore.Key(DateTime.Today.AddDays(2)),
        _ => null
    };

    private void DayToday_Click(object? sender, RoutedEventArgs e) => SetDayView(DayView.Today);
    private void DayTomorrow_Click(object? sender, RoutedEventArgs e) => SetDayView(DayView.Tomorrow);
    private void DayUpcoming_Click(object? sender, RoutedEventArgs e) => SetDayView(DayView.Upcoming);

    private void SetDayView(DayView view)
    {
        _dayView = view;
        RefreshGoalScope();
        RefreshCarryOver();
        UpdateDailyProgress();
        DailyGoalInput.Focus();
    }

    private void RefreshDayTabs()
    {
        foreach (var (tab, view) in new[] { (DayTodayText, DayView.Today), (DayTomorrowText, DayView.Tomorrow), (DayUpcomingText, DayView.Upcoming) })
        {
            tab.Foreground = Brush(view == _dayView ? "MutedTextBrush" : "FaintTextBrush");
            tab.FontWeight = view == _dayView ? FontWeight.SemiBold : FontWeight.Normal;
        }

        var planned = _dailyGoals.Count(g => g.Due == TomorrowKey && !g.Done);
        var later = _dailyGoals.Count(g => g.IsPlannedAfter(TomorrowKey) && !g.Done);
        DayTomorrowCount.Text = planned > 0 ? planned.ToString() : "";
        DayUpcomingCount.Text = later > 0 ? later.ToString() : "";
        // Evenings, a dot nudges toward planning tomorrow while nothing's planned yet.
        PlanNudgeDot.IsVisible = _dayView == DayView.Today && planned == 0 && DateTime.Now.Hour >= 17;

        DailyGoalPlaceholder.Text = _dayView switch
        {
            DayView.Tomorrow => "Plan a task for tomorrow",
            DayView.Upcoming => "Add a task for later",
            _ => "Add a task"
        };
        DailyNotesLabel.IsVisible = DailyNotesBox.IsVisible = _dayView == DayView.Today;
    }

    private void MoveGoalToDay(DailyGoal goal, bool tomorrow)
    {
        goal.Due = tomorrow ? TomorrowKey : null; // saves and syncs the due date
        SyncVisibleGoals();
        UpdateDailyProgress();
    }

    // A new day starts with what an earlier day planned for it.
    private void TakePlannedGoals()
    {
        foreach (var planned in _dailyStore.TakePlannedFor(DateTime.Today))
        {
            if (_dailyGoals.Any(g => g.Id == planned.Id)) continue;
            planned.PropertyChanged += DailyGoal_PropertyChanged;
            _dailyGoals.Add(planned);
        }
    }

    // ---------- Daily target ----------

    private int? _dailyTargetMinutes;
    private const double RingSize = 54;
    private const double RingThickness = 5;

    private int TodayWorkedSeconds => TodayLoggedSeconds() + (IsSessionActive ? _activeSeconds : 0);

    // Today's total on Home, with the ring filling toward the target. Cheap: runs every tick on Home.
    private void UpdateTodayCard()
    {
        var seconds = TodayWorkedSeconds;
        HomeTodayText.Text = FormatSpan(seconds);

        if (_dailyTargetMinutes is not int target)
        {
            DailyTargetArc.Data = null;
            DailyTargetLabel.Text = "+";
            HomeTargetText.Text = "worked today";
            ToolTip.SetTip(DailyTargetButton, "Set a daily target");
            return;
        }

        var fraction = Math.Min(1, seconds / (target * 60.0));
        var done = fraction >= 1;
        DailyTargetArc.Data = RingArc(fraction);
        DailyTargetArc.Stroke = Brush(done ? "SuccessBrush" : "AccentBrush");
        DailyTargetLabel.Text = done ? "✓" : $"{(int)(fraction * 100)}%";
        HomeTargetText.Text = done ? $"{DurationText.Format(target)} target hit" : $"of {DurationText.Format(target)} today";
        ToolTip.SetTip(DailyTargetButton, "Change the daily target");
    }

    private static Geometry? RingArc(double fraction)
    {
        if (fraction <= 0) return null;
        var radius = (RingSize - RingThickness) / 2;
        var center = RingSize / 2;
        if (fraction >= 0.9999) return new EllipseGeometry(new Rect(center - radius, center - radius, radius * 2, radius * 2));

        var angle = fraction * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsFilled = false, IsClosed = false };
        figure.Segments!.Add(new ArcSegment
        {
            Point = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle)),
            Size = new Size(radius, radius),
            IsLargeArc = fraction > 0.5,
            SweepDirection = SweepDirection.Clockwise
        });
        return new PathGeometry { Figures = new PathFigures { figure } };
    }

    private async void DailyTarget_Click(object? sender, RoutedEventArgs e) => await PromptDailyTargetAsync();

    private async Task PromptDailyTargetAsync()
    {
        var text = await PromptWindow.Ask(this, "Daily target", "How long do you want to work each day? Like 4h or 3:30, or 0 for none.",
            _dailyTargetMinutes is int current ? DurationText.Format(current) : null);
        if (text == null) return;

        text = text.Trim();
        var off = text is "" or "0" or "off" or "none";
        var minutes = DurationText.TryParseMinutes(text);
        if (!off && minutes == null) return;

        _dailyTargetMinutes = off ? null : minutes;
        var settings = _settingsStore.Load();
        settings.DailyTargetMinutes = _dailyTargetMinutes;
        _settingsStore.Save(settings);
        UpdateTodayCard();
        BroadcastDeckState();
    }

    // ---------- Quick capture ----------

    private QuickCaptureWindow? _capture;

    private void ShowQuickCapture()
    {
        if (_capture != null)
        {
            _capture.Activate();
            return;
        }

        LoadDailyPlan(); // past midnight, it belongs to the new day
        var lists = TargetsFor(_goalScope).Select(t => (t.Label, (object)t)).ToList();
        _capture = new QuickCaptureWindow(lists, PickedTarget());
        _capture.Captured += (text, tag) =>
        {
            var target = tag as ListTarget ?? PickedTarget();
            AddDailyGoal(new DailyGoal { Text = text, Group = target.Group, ProjectId = target.ProjectId, ProjectName = target.Name });
            UpdateDailyProgress();
        };
        _capture.Closed += (_, _) => _capture = null;
        _capture.Show();
    }
}
