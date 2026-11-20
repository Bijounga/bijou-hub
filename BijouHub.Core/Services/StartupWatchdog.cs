using System.Diagnostics;

namespace BijouHub.Services;

// Starting when Windows signs in is when nothing is ready yet: the sync folder isn't there, the taskbar
// isn't up. If BijouHub stops moving while it starts, it would sit there with no window, no tray icon and
// no way to be opened again. This notices, writes which step it was on to errors.log (so the cause can be
// found), and starts a fresh copy once; a second stuck start just ends instead of piling up.
public sealed class StartupWatchdog
{
    public const string RetryArgument = "--retried";

    private readonly string[] _args;
    private readonly int _limitSeconds;
    private readonly Action<string, bool>? _onStuck;
    private volatile string _step = "starting";
    private volatile bool _done;
    private volatile bool _waiting;

    // `onStuck` (the step, and whether this was already the second try) is for tests; by default it
    // writes the log, starts the fresh copy and ends this one.
    public StartupWatchdog(string[] args, int limitSeconds = 90, Action<string, bool>? onStuck = null)
    {
        _args = args;
        _limitSeconds = limitSeconds;
        _onStuck = onStuck;
    }

    public bool IsRetry => _args.Contains(RetryArgument);

    public void Step(string name) => _step = name;

    // A step that is meant to wait (for the sync folder to turn up) isn't a stuck start.
    public void Waiting(bool waiting) => _waiting = waiting;

    public void Done() => _done = true;

    public void Start()
    {
        new Thread(Watch) { IsBackground = true, Name = "BijouHub startup watchdog" }.Start();
    }

    private void Watch()
    {
        var step = _step;
        var still = 0;
        while (!_done)
        {
            Thread.Sleep(1000);
            if (_done) return;
            if (_waiting || _step != step)
            {
                step = _step;
                still = 0; // moving on, or waiting on purpose: the clock starts again
                continue;
            }
            if (++still < _limitSeconds) continue;

            if (_onStuck != null) _onStuck(step, IsRetry);
            else GiveUp(step);
            return;
        }
    }

    private void GiveUp(string step)
    {
        ErrorLog.Write($"BijouHub got stuck starting up, at: {step}. " + (IsRetry ? "That was already the second try, so it's ending." : "Starting a fresh copy."));
        if (!IsRetry)
        {
            try
            {
                var arguments = string.Join(" ", _args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a).Append(RetryArgument));
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = false });
            }
            catch
            {
                // nothing more to be done
            }
        }
        // Ended the way Task Manager would: a frozen start may not be able to exit politely.
        Process.GetCurrentProcess().Kill();
    }
}
