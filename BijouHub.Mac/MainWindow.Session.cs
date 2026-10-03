using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// The session engine — the same rules as Windows: counts active time (pausing itself after 3
// idle minutes), optional countdown or budget with a time's-up alert, manual pause, extending,
// a checkpoint every 10 s so a crash loses almost nothing, and a pop-out mini timer.
public partial class MainWindow
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(
        double.TryParse(Environment.GetEnvironmentVariable("BIJOUHUB_IDLE_MINUTES"), out var idleMinutes) ? idleMinutes : 3);

    private WorkMode? _activeMode;
    private Project? _activeProject;
    private Goal? _activeGoal;
    private int? _targetMinutes;
    private bool _countDownMode;
    private DateTime _sessionStart;
    private int _activeSeconds;
    private int _idleSeconds;
    private bool _isIdle;
    private bool _paused;
    private bool _timeUpShown;
    private bool _sessionStarting;
    private string? _deckKeyId;
    private DispatcherTimer? _tick;
    private TimerPopoutWindow? _popout;
    private TimeUpWindow? _timeUpWindow;
    private string? _recoveredNotice;

    private bool IsSessionActive => _activeMode != null || _activeProject != null;

    private void InitSession()
    {
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Tick();
        Opened += async (_, _) =>
        {
            if (_recoveredNotice != null) await PromptWindow.Notice(this, "Session recovered", _recoveredNotice);
        };
    }

    private async Task BeginSession(WorkMode? mode, Project? project, Goal? goal, int? targetMinutes, bool countDown = false, string? deckKeyId = null,
        PomodoroPlan? pomodoro = null)
    {
        // Starting while another session runs replaces it — the running one is logged first.
        if (IsSessionActive) await FinishSessionAsync(askForNote: false);

        if (mode != null)
        {
            var failures = await MacPlatform.LaunchAsync(mode);
            if (failures.Count > 0)
                _ = PromptWindow.Notice(this, "Launch", "Some items in this mode didn't open:\n\n" + string.Join("\n", failures));
        }

        if (mode?.DoNotDisturb == true) MacDoNotDisturb.TurnOn();

        _activeMode = mode;
        _activeProject = project;
        _activeGoal = goal;
        StartPomodoro(pomodoro);
        _targetMinutes = pomodoro == null ? targetMinutes : null;
        _countDownMode = countDown && _targetMinutes != null;
        _sessionStart = DateTime.Now;
        _activeSeconds = 0;
        _idleSeconds = 0;
        _isIdle = false;
        _paused = false;
        _timeUpShown = false;
        _deckKeyId = deckKeyId;
        TimeUpBanner.IsVisible = false;
        _timeUpWindow?.Close();

        SessionTitleText.Text = project?.Name ?? mode?.Name ?? "Session";
        UpdateSessionContext();
        SessionStatusText.Text = _pomodoro != null ? PomodoroStatus : "Active";
        SetPauseButton();
        SessionTimerText.Text = TimerDisplay;
        ShowSession();
        _tick!.Start();
        WriteCheckpoint();
        BroadcastDeckState();
    }

    private void ShowSession()
    {
        _detailProject = _activeProject;
        ShowOnly(SessionPanel);
    }

    private void UpdateSessionContext()
    {
        var parts = new List<string>();
        if (_activeProject != null && _activeMode != null) parts.Add($"via {_activeMode.Name}");
        if (_activeGoal != null) parts.Add($"working on: {_activeGoal.Name}");
        if (_targetMinutes is int t) parts.Add(_countDownMode ? $"counting down from {DurationText.Format(t)}" : $"budget: {DurationText.Format(t)}");
        if (_pomodoro != null) parts.Add($"Pomodoro {_pomodoro.FocusMinutes} / {_pomodoro.BreakMinutes} min");
        if (_activeMode?.DoNotDisturb == true) parts.Add("Do Not Disturb");
        SessionContextText.Text = string.Join("  •  ", parts);
        SessionContextText.IsVisible = parts.Count > 0;
    }

    private static string Clock(int seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");

    private string TimerDisplay => _pomodoro != null ? PomodoroClock
        : _countDownMode && _targetMinutes is int target
        ? (_activeSeconds <= target * 60 ? Clock(target * 60 - _activeSeconds) : "+" + Clock(_activeSeconds - target * 60))
        : Clock(_activeSeconds);

    private void Tick()
    {
        if (_paused || _onBreak)
        {
            _idleSeconds++; // paused time (and a Pomodoro break) isn't worked time
        }
        else if (MacPlatform.IdleTime() >= IdleThreshold)
        {
            _idleSeconds++;
            if (!_isIdle)
            {
                _isIdle = true;
                SessionStatusText.Text = "Away — timer paused";
            }
        }
        else
        {
            _activeSeconds++;
            if (_isIdle)
            {
                _isIdle = false;
                SessionStatusText.Text = "Active";
            }
        }

        if (_pomodoro != null)
        {
            if (!_paused) AdvancePomodoro();
            SessionStatusText.Text = _paused ? "Paused" : _onBreak ? PomodoroStatus : _isIdle ? "Away — timer paused" : PomodoroStatus;
        }
        SessionTimerText.Text = TimerDisplay;
        _popout?.Update(SessionTitleText.Text ?? "", TimerDisplay, SessionStatusText.Text ?? "");
        if (HomePanel.IsVisible) UpdateTodayCard();

        if (_targetMinutes is int target && !_timeUpShown && _activeSeconds >= target * 60) ShowTimeUp(target);
        if ((_activeSeconds + _idleSeconds) % 10 == 0) WriteCheckpoint();
        BroadcastDeckState();
    }

    private void ShowTimeUp(int target)
    {
        _timeUpShown = true;
        var title = SessionTitleText.Text ?? "the session";
        TimeUpText.Text = $"Time's up — {DurationText.Format(target)} on {title}.";
        TimeUpBanner.IsVisible = true;
        Chime();

        // Also a small always-on-top alert, so it's seen while another app (Premiere…) is in front.
        _timeUpWindow?.Close();
        _timeUpWindow = new TimeUpWindow(title, target);
        _timeUpWindow.Extended += () => ExtendSession(15);
        _timeUpWindow.FinishRequested += () => _ = FinishSessionAsync(askForNote: true);
        _timeUpWindow.Show();
    }

    private static void Chime(string sound = "Glass")
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("/usr/bin/afplay", $"/System/Library/Sounds/{sound}.aiff") { UseShellExecute = false });
        }
        catch
        {
            // No sound is fine.
        }
    }

    // Adds time: more countdown (or budget), or a countdown from now for a count-up session.
    private bool ExtendSession(int minutes)
    {
        if (!IsSessionActive || minutes <= 0) return false;
        if (_pomodoro != null)
        {
            ExtendPomodoroPhase(minutes);
            SessionTimerText.Text = TimerDisplay;
            BroadcastDeckState();
            return true;
        }
        var elapsed = (int)Math.Ceiling(_activeSeconds / 60.0);
        if (_targetMinutes is int target)
        {
            _targetMinutes = Math.Max(target, elapsed) + minutes;
        }
        else
        {
            _targetMinutes = elapsed + minutes;
            _countDownMode = true;
        }
        _timeUpShown = false;
        TimeUpBanner.IsVisible = false;
        _timeUpWindow?.Close();
        _timeUpWindow = null;
        UpdateSessionContext();
        SessionTimerText.Text = TimerDisplay;
        BroadcastDeckState();
        return true;
    }

    private void Extend15_Click(object? sender, RoutedEventArgs e) => ExtendSession(15);

    private void TogglePause()
    {
        if (!IsSessionActive) return;
        _paused = !_paused;
        _isIdle = false;
        SessionStatusText.Text = _paused ? "Paused" : _pomodoro != null ? PomodoroStatus : "Active";
        SetPauseButton();
        _popout?.Update(SessionTitleText.Text ?? "", TimerDisplay, SessionStatusText.Text ?? "");
        BroadcastDeckState();
    }

    private void SetPauseButton()
    {
        PauseIcon.Kind = _paused ? "play" : "pause";
        PauseIcon.Filled = _paused;
        ToolTip.SetTip(PauseButton, _paused ? "Resume" : "Pause");
    }

    private void Pause_Click(object? sender, RoutedEventArgs e) => TogglePause();

    private async void FinishSession_Click(object? sender, RoutedEventArgs e) => await FinishSessionAsync(askForNote: true);

    // askForNote is false when the deck (or a replacement session) ends it — no modal mid-work.
    // Without the note prompt this completes synchronously.
    private async Task FinishSessionAsync(bool askForNote)
    {
        if (!IsSessionActive) return;
        _tick!.Stop();
        _popout?.Close();
        _timeUpWindow?.Close();
        _timeUpWindow = null;
        TimeUpBanner.IsVisible = false;

        var project = _activeProject;
        var mode = _activeMode;
        var record = new SessionRecord
        {
            ModeName = mode?.Name ?? "",
            StartTime = _sessionStart,
            EndTime = DateTime.Now,
            ActiveSeconds = _activeSeconds,
            IdleSeconds = _idleSeconds,
            ProjectId = project?.Id,
            ProjectName = project?.Name,
            GoalId = _activeGoal?.Id,
            GoalName = _activeGoal?.Name
        };
        _activeMode = null;
        _activeProject = null;
        _activeGoal = null;
        _targetMinutes = null;
        _countDownMode = false;
        _paused = false;
        _deckKeyId = null;
        StartPomodoro(null);
        MacDoNotDisturb.Restore();
        BroadcastDeckState();

        if (project != null && askForNote)
        {
            var dialog = new FinishNoteWindow();
            if (await dialog.ShowDialog<bool>(this) && !string.IsNullOrEmpty(dialog.Note))
            {
                record.Note = dialog.Note;
                project.Notes.Add(new ProjectNote { Text = dialog.Note });
                _projectStore.Save(_projects.ToList());
            }
        }

        _logService.InsertSession(record);
        InvalidateTodayLogged();
        ClearCheckpoint();

        if (project != null) SelectProject(project);
        else if (mode != null && ModesList.SelectedItem == mode) ShowMode(mode);
        else ShowHome();
    }

    // ---------- Pop-out mini timer ----------

    private void Popout_Click(object? sender, RoutedEventArgs e) => SetTimerPopout(_popout == null);

    // Pops the mini timer out (always on top, for working in another app) or docks it again.
    private void SetTimerPopout(bool open)
    {
        if (!open)
        {
            _popout?.Close();
            return;
        }
        if (_popout != null) return;

        _popout = new TimerPopoutWindow();
        _popout.Update(SessionTitleText.Text ?? "", TimerDisplay, SessionStatusText.Text ?? "");
        _popout.PauseRequested += TogglePause;
        _popout.FinishRequested += () => _ = FinishSessionAsync(askForNote: true);
        _popout.Closed += (_, _) =>
        {
            _popout = null;
            PopoutIcon.Kind = "popout";
            ToolTip.SetTip(PopoutButton, "Pop out the timer");
            BroadcastDeckState();
        };
        PopoutIcon.Kind = "dock";
        ToolTip.SetTip(PopoutButton, "Dock the timer");
        _popout.Show();
        BroadcastDeckState();
    }

    // ---------- Crash recovery ----------

    private static string CheckpointPath => Path.Combine(DataPaths.LocalDir, "active_session.json");

    private void WriteCheckpoint()
    {
        var checkpoint = new SessionCheckpoint
        {
            StartTime = _sessionStart,
            ModeName = _activeMode?.Name ?? "",
            ProjectId = _activeProject?.Id,
            ProjectName = _activeProject?.Name,
            GoalId = _activeGoal?.Id,
            GoalName = _activeGoal?.Name,
            ActiveSeconds = _activeSeconds,
            IdleSeconds = _idleSeconds
        };
        try { AtomicFile.WriteAllText(CheckpointPath, JsonSerializer.Serialize(checkpoint)); }
        catch { /* a missed checkpoint only means a little more to lose on a crash */ }
    }

    private static void ClearCheckpoint()
    {
        try { File.Delete(CheckpointPath); }
        catch { /* nothing to clean up */ }
    }

    private void FinalizeSessionSilently()
    {
        _tick?.Stop();
        MacDoNotDisturb.Restore();
        if (_activeSeconds > 0)
        {
            _logService.InsertSession(new SessionRecord
            {
                ModeName = _activeMode?.Name ?? "",
                StartTime = _sessionStart,
                EndTime = DateTime.Now,
                ActiveSeconds = _activeSeconds,
                IdleSeconds = _idleSeconds,
                ProjectId = _activeProject?.Id,
                ProjectName = _activeProject?.Name,
                GoalId = _activeGoal?.Id,
                GoalName = _activeGoal?.Name
            });
        }
        ClearCheckpoint();
    }

    private void RecoverInterruptedSession()
    {
        if (!File.Exists(CheckpointPath)) return;
        try
        {
            var checkpoint = JsonSerializer.Deserialize<SessionCheckpoint>(File.ReadAllText(CheckpointPath));
            if (checkpoint is { ActiveSeconds: > 0 })
            {
                _logService.InsertSession(new SessionRecord
                {
                    ModeName = checkpoint.ModeName,
                    StartTime = checkpoint.StartTime,
                    EndTime = checkpoint.StartTime.AddSeconds(checkpoint.ActiveSeconds + checkpoint.IdleSeconds),
                    ActiveSeconds = checkpoint.ActiveSeconds,
                    IdleSeconds = checkpoint.IdleSeconds,
                    ProjectId = checkpoint.ProjectId,
                    ProjectName = checkpoint.ProjectName,
                    GoalId = checkpoint.GoalId,
                    GoalName = checkpoint.GoalName,
                    Note = "Recovered after BijouHub closed unexpectedly"
                });
                var minutes = Math.Max(1, checkpoint.ActiveSeconds / 60);
                _recoveredNotice = $"BijouHub didn't close normally last time — recovered {minutes} minute{(minutes == 1 ? "" : "s")} of tracked time" +
                                   (checkpoint.ProjectName != null ? $" on \"{checkpoint.ProjectName}\"." : ".");
            }
        }
        catch
        {
            // Corrupt checkpoint — nothing usable to recover.
        }
        finally
        {
            ClearCheckpoint();
        }
    }
}
