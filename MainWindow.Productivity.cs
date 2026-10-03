using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

// Planning tomorrow, the daily target ring and Quick Capture.
public partial class MainWindow
{
    // ---------- Plan tomorrow ----------

    // The goal list shows today (goals planned for a later day hidden) or tomorrow's plan.
    private bool _viewTomorrow;

    private static string TodayKey => DailyPlanStore.Key(DateTime.Today);
    private static string TomorrowKey => DailyPlanStore.Key(DateTime.Today.AddDays(1));

    private bool InDay(DailyGoal goal) => _viewTomorrow ? goal.Due == TomorrowKey : !goal.IsPlannedAfter(TodayKey);

    private void DayToday_Click(object sender, RoutedEventArgs e) => SetDayView(false);
    private void DayTomorrow_Click(object sender, RoutedEventArgs e) => SetDayView(true);

    private void SetDayView(bool tomorrow)
    {
        _viewTomorrow = tomorrow;
        RefreshGoalScope();
        RefreshCarryOver();
        UpdateDailyProgress();
        DailyGoalInput.Focus();
    }

    private void RefreshDayTabs()
    {
        DayTodayText.SetResourceReference(ForegroundProperty, _viewTomorrow ? "FaintTextBrush" : "MutedTextBrush");
        DayTomorrowText.SetResourceReference(ForegroundProperty, _viewTomorrow ? "MutedTextBrush" : "FaintTextBrush");
        DayTodayText.FontWeight = _viewTomorrow ? FontWeights.Normal : FontWeights.SemiBold;
        DayTomorrowText.FontWeight = _viewTomorrow ? FontWeights.SemiBold : FontWeights.Normal;

        var planned = _dailyGoals.Count(g => g.Due == TomorrowKey && !g.Done);
        DayTomorrowCount.Text = planned > 0 ? planned.ToString() : "";
        // Evenings, a dot nudges toward planning tomorrow while nothing's planned yet.
        PlanNudgeDot.Visibility = !_viewTomorrow && planned == 0 && DateTime.Now.Hour >= 17 ? Visibility.Visible : Visibility.Collapsed;

        DailyGoalPlaceholder.Text = _viewTomorrow ? "Plan a task for tomorrow" : "Add a task";
        DailyNotesLabel.Visibility = DailyNotesBox.Visibility = _viewTomorrow ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MoveGoalToDay(DailyGoal goal, bool tomorrow)
    {
        goal.Due = tomorrow ? TomorrowKey : null; // saves and syncs the due date
        CollectionViewSource.GetDefaultView(_dailyGoals).Refresh();
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
            DailyTargetButton.ToolTip = "Set a daily target";
            return;
        }

        var fraction = Math.Min(1, seconds / (target * 60.0));
        var done = fraction >= 1;
        DailyTargetArc.Data = RingArc(fraction);
        DailyTargetArc.SetResourceReference(Shape.StrokeProperty, done ? "SuccessBrush" : "AccentBrush");
        DailyTargetLabel.Text = done ? "✓" : $"{(int)(fraction * 100)}%";
        HomeTargetText.Text = done ? $"{DurationText.Format(target)} target hit" : $"of {DurationText.Format(target)} today";
        DailyTargetButton.ToolTip = "Change the daily target";
    }

    private static Geometry? RingArc(double fraction)
    {
        if (fraction <= 0) return null;
        var radius = (RingSize - RingThickness) / 2;
        var center = RingSize / 2;
        if (fraction >= 0.9999) return new EllipseGeometry(new Point(center, center), radius, radius);

        var angle = fraction * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle)),
            new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }

    private void DailyTarget_Click(object sender, RoutedEventArgs e) => PromptDailyTarget();

    private void PromptDailyTarget()
    {
        var now = _dailyTargetMinutes is int current ? $" It's {DurationText.Format(current)} now." : "";
        var dialog = new TextPromptWindow("Daily target", $"How long do you want to work each day? Like 4h or 3:30, or 0 for none.{now}") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var text = dialog.Value.Trim();
        var off = text is "0" or "off" or "none";
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
