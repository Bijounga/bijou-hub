namespace BijouHub.Services;

// Focus for FocusMinutes, break for BreakMinutes, repeat, until the session is finished.
public sealed record PomodoroPlan(int FocusMinutes, int BreakMinutes)
{
    // The way it's typed and saved: "25/5".
    public override string ToString() => $"{FocusMinutes}/{BreakMinutes}";
}
