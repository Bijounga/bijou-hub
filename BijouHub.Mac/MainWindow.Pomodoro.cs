using System.Text.Json.Nodes;
using BijouHub.Services;

namespace BijouHub.Mac;

// Pomodoro sessions: focus for a while, break for a while, repeat until finished. Break time is
// logged as idle (not worked), like a pause. Started from a "25/5" timer on a mode or from the
// Stream Deck's Pomodoro key. Same rules as the Windows app.
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
            Mac.Services.SoundFx.Play(Mac.Services.SoundFx.Complete);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _pomodoroFinishing = false;
                _ = FinishSessionAsync(askForNote: false);
            });
            return;
        }

        _onBreak = !_onBreak;
        if (!_onBreak) _pomodoroRound++;
        _phaseLeft = _phaseLength = (_onBreak ? _pomodoro.BreakMinutes : _pomodoro.FocusMinutes) * 60;
        Mac.Services.SoundFx.Play(_onBreak ? Mac.Services.SoundFx.Break : Mac.Services.SoundFx.Focus);
        UpdateSessionContext();
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

    private JsonObject? DeckPomodoro() => _pomodoro == null ? null : new JsonObject
    {
        ["phase"] = _onBreak ? "break" : "focus",
        ["round"] = _pomodoroRound,
        ["rounds"] = _pomodoro.Rounds,
        ["remaining"] = _phaseLeft,
        ["phaseSeconds"] = _phaseLength
    };
}
