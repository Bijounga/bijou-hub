using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BijouHub.Services;

// One BijouHub per data folder. Starting it again (the app icon, a Stream Deck "open app" button,
// a Start-menu click) while it's already running, even hidden in the tray, doesn't start a second
// copy: it asks the running one to show its window, and exits. A test run with its own data
// folder (BIJOUHUB_DATA_DIR) gets its own name, so it never meets the real one.
public sealed class SingleInstance
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly EventWaitHandle _shown;
    private volatile bool _stopped;

    public bool IsFirst { get; }

    private SingleInstance(string name)
    {
        _mutex = new Mutex(true, name, out var created);
        IsFirst = created;
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-show");
        _shown = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-shown");
    }

    public static SingleInstance Acquire(string dataFolder)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(dataFolder.ToLowerInvariant())))[..12];
        return new SingleInstance(@"Local\BijouHub-" + hash);
    }

    // The first instance: calls `onShow` (on a background thread) each time another start asks it to.
    public void Listen(Action onShow)
    {
        var thread = new Thread(() =>
        {
            while (!_stopped)
            {
                _show.WaitOne();
                if (_stopped) break;
                try { onShow(); }
                catch { /* the window may be closing: nothing to show */ }
                finally { _shown.Set(); }
            }
        })
        { IsBackground = true, Name = "BijouHub single instance" };
        thread.Start();
    }

    public void StopListening()
    {
        _stopped = true;
        _show.Set();
    }

    // A later start: ask the running one to show itself. True once it has (or it's gone and this one
    // can take over: the running one may be in the middle of quitting).
    public bool HandOver(TimeSpan wait)
    {
        _show.Set();
        if (_shown.WaitOne(wait)) return true;
        return false;
    }

    // The running one never answered: it may be quitting. Wait for it to let go, then this one is first.
    public bool TakeOver(TimeSpan wait)
    {
        try { return _mutex.WaitOne(wait); }
        catch (AbandonedMutexException) { return true; } // the old one died holding it
    }
}
