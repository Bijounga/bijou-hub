using System.Diagnostics;
using System.Windows.Threading;
using BijouHub.Models;

namespace BijouHub.Services;

public class BlockWatcher
{
    private readonly DispatcherTimer _timer;
    private readonly HashSet<int> _seenPids = new();
    private readonly HashSet<string> _allowedNames = new(StringComparer.OrdinalIgnoreCase);
    private WorkMode? _mode;

    public event Action<string, int>? NewBlockedProcessDetected;
    public event Action<string>? HardBlockedProcessClosed;

    public BlockWatcher()
    {
        // Polls fast enough that a hard-blocked app gets killed well within a second of
        // launching, rather than sitting open for a couple seconds like the soft-block nag.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start(WorkMode mode)
    {
        _mode = mode;
        _seenPids.Clear();
        _allowedNames.Clear();

        // Snapshot processes already running when the mode starts so we only
        // flag ones that get launched *after* the mode begins.
        foreach (var block in mode.BlockItems)
        {
            foreach (var proc in Process.GetProcessesByName(block.ProcessName))
                _seenPids.Add(proc.Id);
        }

        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _mode = null;
    }

    public void AllowTemporarily(string processName, TimeSpan duration)
    {
        _allowedNames.Add(processName);
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            _allowedNames.Remove(processName);
            timer.Stop();
        };
        timer.Start();
    }

    public static void CloseProcess(string processName)
    {
        foreach (var proc in Process.GetProcessesByName(processName))
        {
            try { proc.CloseMainWindow(); }
            catch { /* process may not have a main window; ignore */ }
        }
    }

    private void Poll()
    {
        if (_mode == null) return;

        foreach (var block in _mode.BlockItems)
        {
            // Hard blocks ignore the temporary-allow list entirely — the only way past one
            // is ending the session, per how this was asked for.
            if (!block.IsHardBlock && _allowedNames.Contains(block.ProcessName))
                continue;

            foreach (var proc in Process.GetProcessesByName(block.ProcessName))
            {
                if (_seenPids.Contains(proc.Id))
                    continue;

                _seenPids.Add(proc.Id);

                if (block.IsHardBlock)
                {
                    try
                    {
                        proc.Kill();
                        HardBlockedProcessClosed?.Invoke(block.ProcessName);
                    }
                    catch
                    {
                        // Already exited, access denied, etc. — nothing more to do.
                    }
                }
                else
                {
                    NewBlockedProcessDetected?.Invoke(block.ProcessName, proc.Id);
                }
            }
        }
    }
}
