using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

// The kinder side of the timer: a summary of what got done when a session ends, a "still on it?" card
// when you've been somewhere distracting for a few minutes, and a way to close out the day.
public partial class MainWindow
{
    // ---------- Wins: after a session ----------

    private bool _skipWinsOnce;

    private void ShowWins(SessionRecord finished)
    {
        if (_skipWinsOnce) { _skipWinsOnce = false; return; }
        var settings = _settingsStore.Load();
        if (!settings.ShowWins || finished.ActiveSeconds < 60) return;

        var summary = WinsSummaryBuilder.AfterSession(finished, _logService.GetAll(), _dailyTargetMinutes, TasksDoneToday(), DateTime.Now);
        var context = finished.TaskName ?? finished.ProjectName ?? (string.IsNullOrEmpty(finished.ModeName) ? null : finished.ModeName);
        var window = new WinsWindow(summary, context) { Owner = DialogOwner };
        window.ShowDialog();
        if (!window.KeepShowingAfter)
        {
            settings.ShowWins = false;
            _settingsStore.Save(settings);
        }
    }

    private int TasksDoneToday() => _dailyGoals.Count(g => g.Done && g.CompletedAt?.Date == DateTime.Today);

    // ---------- Drift: "still on it?" ----------

    // BIJOUHUB_DRIFT_SECONDS shortens the wait so a test doesn't need minutes.
    private readonly DriftWatch _drift = new(int.TryParse(Environment.GetEnvironmentVariable("BIJOUHUB_DRIFT_SECONDS"), out var s) && s > 0 ? s : 150);
    private DriftNudgeWindow? _nudge;
    private bool _driftEnabled;
    private HashSet<string> _driftIgnored = new(StringComparer.OrdinalIgnoreCase);
    private string? _driftWhat;

    private static readonly string[] Browsers = { "chrome", "msedge", "brave", "firefox", "opera", "vivaldi", "arc" };
    private static readonly string[] DriftWords = { "YouTube", "Reddit", "Twitter", " / X", "TikTok", "Instagram", "Twitch", "Netflix", "Facebook" };

    private void StartDriftWatch()
    {
        var settings = _settingsStore.Load();
        _driftEnabled = settings.DriftNudge;
        _driftIgnored = new HashSet<string>(settings.DriftIgnored, StringComparer.OrdinalIgnoreCase);
        _drift.Reset();
        _driftWhat = null;
    }

    private void StopDriftWatch()
    {
        _driftEnabled = false;
        _nudge?.Close();
        _nudge = null;
    }

    // One second of working time: is the window in front somewhere the mode says not to be?
    private void DriftTick()
    {
        if (!_driftEnabled || _nudge != null) return;
        var what = DriftMatch(ForegroundApp.Current());
        if (what != null) _driftWhat = what;
        if (_drift.Tick(what != null, DateTime.Now) && _driftWhat != null) ShowNudge(_driftWhat);
    }

    // The mode's blocked apps count wherever they are; a distracting site counts only in a browser,
    // so a folder called "YouTube videos" or a project file doesn't set it off.
    private string? DriftMatch(ForegroundApp.Info? front)
    {
        if (front == null) return null;
        if (_activeMode?.BlockItems.Any(b => string.Equals(b.ProcessName, front.Process, StringComparison.OrdinalIgnoreCase)) == true
            && !_driftIgnored.Contains(front.Process))
            return front.Process;

        if (Browsers.Contains(front.Process, StringComparer.OrdinalIgnoreCase))
            foreach (var word in DriftWords)
                if (front.Title.Contains(word, StringComparison.OrdinalIgnoreCase) && !_driftIgnored.Contains(word.Trim(' ', '/')))
                    return word.Trim(' ', '/');
        return null;
    }

    private void ShowNudge(string what)
    {
        var workingOn = _activeTask?.Text ?? _activeProject?.Name ?? _activeMode?.Name ?? "it";
        var nudge = new DriftNudgeWindow(workingOn, what);
        var workArea = SystemParameters.WorkArea;
        nudge.Left = workArea.Right - nudge.Width - 16;
        nudge.Top = workArea.Bottom - nudge.Height - 16;
        nudge.BackToWork += () => { _drift.Reset(); nudge.Close(); };
        nudge.Snoozed += () => { _drift.Snooze(TimeSpan.FromMinutes(5), DateTime.Now); nudge.Close(); };
        nudge.TakeBreak += () => { if (!_paused) TogglePause(); nudge.Close(); };
        nudge.NotDistraction += () =>
        {
            _driftIgnored.Add(what);
            var settings = _settingsStore.Load();
            if (!settings.DriftIgnored.Contains(what, StringComparer.OrdinalIgnoreCase)) settings.DriftIgnored.Add(what);
            _settingsStore.Save(settings);
            nudge.Close();
        };
        nudge.Closed += (_, _) => { if (_nudge == nudge) _nudge = null; };
        _nudge = nudge;
        nudge.Show();
    }

    // ---------- Wrapping up the day ----------

    private void WrapUpDay_Click(object sender, RoutedEventArgs e)
    {
        if (IsSessionActive)
        {
            _skipWinsOnce = true; // the summary is the first thing the wrap-up shows
            FinishSession(askForNote: true);
        }

        var history = _logService.GetAll();
        var summary = WinsSummaryBuilder.ForDay(history, _dailyTargetMinutes, TasksDoneToday(), DateTime.Now);
        var lastProject = history.Where(r => r.ProjectId != null)
            .Select(r => _projects.FirstOrDefault(p => p.Id == r.ProjectId)).FirstOrDefault(p => p != null);

        var window = new ShutdownWindow(summary, lastProject?.Name, RunningWorkApps()) { Owner = DialogOwner };
        if (window.ShowDialog() != true) return;

        if (window.WentWell.Length > 0)
            DailyNotesBox.Text = QuickNotes.Append(DailyNotesBox.Text, "Went well: " + window.WentWell);

        if (window.FirstThing.Length > 0)
        {
            AddDailyGoal(new DailyGoal
            {
                Text = window.FirstThing,
                Due = TomorrowKey,
                ProjectId = lastProject?.Id,
                ProjectName = lastProject?.Name,
            });
            RefreshGoalViews();
            UpdateDailyProgress();
        }

        // The same "Did / Next" note the end-of-session prompt writes, so the project's Up next card has it.
        if (lastProject != null && (window.WentWell.Length > 0 || window.FirstThing.Length > 0))
        {
            var parts = new List<string>();
            if (window.WentWell.Length > 0) parts.Add($"Did: {window.WentWell}");
            if (window.FirstThing.Length > 0) parts.Add($"Next: {window.FirstThing}");
            lastProject.Notes.Add(new ProjectNote { Text = string.Join("  |  ", parts) });
            _projectStore.Save(_projects);
            PersistAndRefreshProjectList();
        }

        foreach (var name in window.AppsToClose)
            foreach (var process in Process.GetProcessesByName(name))
            {
                try { process.CloseMainWindow(); } // politely: an app with unsaved work still asks
                catch { /* already gone */ }
            }
    }

    // The apps the work modes launch, if they're open right now.
    private List<string> RunningWorkApps()
    {
        var names = _modes.SelectMany(m => m.LaunchItems)
            .Where(i => i.Type == LaunchItemType.Application)
            .Select(i => Path.GetFileNameWithoutExtension(i.Path))
            .Where(n => !string.IsNullOrEmpty(n) && !n.Equals("BijouHub", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return names.Where(n => Process.GetProcessesByName(n).Any(p => p.MainWindowHandle != IntPtr.Zero)).ToList();
    }
}
