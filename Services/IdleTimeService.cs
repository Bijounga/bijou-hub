using System.Runtime.InteropServices;

namespace BijouHub.Services;

public static class IdleTimeService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static TimeSpan GetIdleTime()
    {
        var lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(lii);
        if (!GetLastInputInfo(ref lii))
            return TimeSpan.Zero;

        var idleTicks = (uint)Environment.TickCount - lii.dwTime;
        return TimeSpan.FromMilliseconds(idleTicks);
    }
}
