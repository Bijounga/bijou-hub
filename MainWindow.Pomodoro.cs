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

    // One second of a running (unpaused) Pomodoro; switches between focus and break at zero.
    private void AdvancePomodoro()
    {
        if (_pomodoro == null || --_phaseLeft > 0) return;

        _onBreak = !_onBreak;
        if (!_onBreak) _pomodoroRound++;
        _phaseLeft = _phaseLength = (_onBreak ? _pomodoro.BreakMinutes : _pomodoro.FocusMinutes) * 60;
        (_onBreak ? System.Media.SystemSounds.Asterisk : System.Media.SystemSounds.Exclamation).Play();
        UpdateSessionContextText();
    }

    private string PomodoroClock =>
        _phaseLeft >= 3600 ? TimeSpan.FromSeconds(_phaseLeft).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(_phaseLeft).ToString(@"mm\:ss");

    private string PomodoroStatus => _onBreak ? "Break" : $"Focus · round {_pomodoroRound}";

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
        ["remaining"] = _phaseLeft,
        ["phaseSeconds"] = _phaseLength
    };
}
