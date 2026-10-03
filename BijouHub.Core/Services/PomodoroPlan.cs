namespace BijouHub.Services;

// Focus for FocusMinutes, break for BreakMinutes, repeat: for Rounds rounds (then the session
// finishes and is logged), or until it's finished by hand when Rounds is null.
public sealed record PomodoroPlan(int FocusMinutes, int BreakMinutes, int? Rounds = null)
{
    // The way it's typed and saved: "25/5", or "25/5x4" with a number of rounds.
    public override string ToString() => Rounds is int rounds ? $"{FocusMinutes}/{BreakMinutes}x{rounds}" : $"{FocusMinutes}/{BreakMinutes}";
}
