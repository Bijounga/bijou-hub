using System.Text.Json.Nodes;
using BijouHub.Services;

namespace BijouHub;

// Pomodoro sessions: focus for a while, break for a while, repeat until finished. Break time is
// logged as idle (not worked), like a pause. Started from a "25/5" timer on a mode or from the
// Stream Deck's Pomodoro key.
public partial class MainWindow
{
    private PomodoroPlan? _pomodoro;
    private bool _onBreak;
    private int _pomodoroRound;
    private int _phaseLeft; // seconds left in the current focus or break
    private int _phaseLength; // its full length, for the deck's ring (Add Time stretches it)

    private void StartPomodoro(PomodoroPlan? plan)
    {
        _pomodoro = plan;
        _onBreak = false;
        _pomodoroRound = plan == null ? 0 : 1;
        _phaseLeft = _phaseLength = plan == null ? 0 : plan.FocusMinutes * 60;
    }

    private bool _pomodoroFinishing;

    // One second of a running (unpaused) Pomodoro; switches between focus and break at zero, and
    // after the last round's focus finishes the session (no break after the last one).
    private void AdvancePomodoro()
    {
        if (_pomodoro == null || _pomodoroFinishing || --_phaseLeft > 0) return;

        if (!_onBreak && _pomodoro.Rounds is int rounds && _pomodoroRound >= rounds)
        {
            _phaseLeft = 0;
            _pomodoroFinishing = true;
            SoundFx.Play(SoundFx.Complete);
            Dispatcher.BeginInvoke(() =>
            {
                _pomodoroFinishing = false;
                FinishSession(askForNote: false);
            });
            return;
        }

        _onBreak = !_onBreak;
        if (!_onBreak) _pomodoroRound++;
        _phaseLeft = _phaseLength = (_onBreak ? _pomodoro.BreakMinutes : _pomodoro.FocusMinutes) * 60;
        SoundFx.Play(_onBreak ? SoundFx.Break : SoundFx.Focus);
        UpdateSessionContextText();
    }

    private string PomodoroClock =>
        _phaseLeft >= 3600 ? TimeSpan.FromSeconds(_phaseLeft).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(_phaseLeft).ToString(@"mm\:ss");

    private string PomodoroStatus =>
        _onBreak ? "Break"
        : _pomodoro?.Rounds is int rounds ? $"Focus · round {_pomodoroRound} of {rounds}"
        : $"Focus · round {_pomodoroRound}";

    // Add Time on a Pomodoro stretches the current focus or break.
    private void ExtendPomodoroPhase(int minutes)
    {
        _phaseLeft += minutes * 60;
        _phaseLength += minutes * 60;
    }

    // The taskbar button fills as the focus (or countdown) runs: green while working, yellow on a
    // break or pause, red once over time. Nothing for an open-ended count-up.
    private void UpdateTaskbarProgress()
    {
        TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
        var info = TaskbarItemInfo;
        if (!IsSessionActive)
        {
            info.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
            return;
        }
        if (_pomodoro != null)
        {
            info.ProgressValue = _phaseLength > 0 ? 1 - (double)_phaseLeft / _phaseLength : 0;
            info.ProgressState = _paused || _onBreak ? System.Windows.Shell.TaskbarItemProgressState.Paused : System.Windows.Shell.TaskbarItemProgressState.Normal;
        }
        else if (_countDownMode && _targetMinutes is int target)
        {
            var total = target * 60.0;
            info.ProgressValue = Math.Min(1, _activeSeconds / total);
            info.ProgressState = _activeSeconds >= total ? System.Windows.Shell.TaskbarItemProgressState.Error
                : _paused ? System.Windows.Shell.TaskbarItemProgressState.Paused
                : System.Windows.Shell.TaskbarItemProgressState.Normal;
        }
        else
        {
            info.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
        }
    }

    private JsonObject? DeckPomodoro() => _pomodoro == null ? null : new JsonObject
    {
        ["phase"] = _onBreak ? "break" : "focus",
        ["round"] = _pomodoroRound,
        ["rounds"] = _pomodoro.Rounds,
        ["remaining"] = _phaseLeft,
        ["phaseSeconds"] = _phaseLength
    };
}
