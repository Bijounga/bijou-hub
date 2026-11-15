namespace BijouHub.Services;

// Decides when to ask "still working?": feed it one second at a time while a session is counting, saying
// whether the thing in front is a distraction. A few minutes in total on distractions trips it, a quick
// look at an asset or a song doesn't, and time on the right thing wears the count down again.
public class DriftWatch
{
    private readonly int _triggerSeconds;
    private int _drift;
    private DateTime _snoozedUntil = DateTime.MinValue;

    public DriftWatch(int triggerSeconds = 150) => _triggerSeconds = triggerSeconds;

    public int Seconds => _drift;

    // True when the nudge is due now.
    public bool Tick(bool drifting, DateTime now)
    {
        if (now < _snoozedUntil) { _drift = 0; return false; }
        if (drifting) _drift++;
        else _drift = Math.Max(0, _drift - 3); // back on track wears it down faster than it builds up

        if (_drift < _triggerSeconds) return false;
        _drift = 0;
        return true;
    }

    public void Snooze(TimeSpan time, DateTime now)
    {
        _drift = 0;
        _snoozedUntil = now + time;
    }

    public void Reset()
    {
        _drift = 0;
        _snoozedUntil = DateTime.MinValue;
    }
}
