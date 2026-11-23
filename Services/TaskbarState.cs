using System.Runtime.InteropServices;

namespace BijouHub.Services;

// Windows' own "Automatically hide the taskbar" setting: on, the taskbar slides away until the pointer
// goes to the screen edge. Switching it is the same as switching it in Windows' taskbar settings, and
// stays as it was left.
public static class TaskbarState
{
    private const uint GetState = 4;
    private const uint SetState = 10;
    private const int AutoHideFlag = 1;
    private const int AlwaysOnTopFlag = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Bounds;
        public IntPtr Param;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);

    private static AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = FindWindow("Shell_TrayWnd", null) };

    public static bool AutoHide
    {
        get
        {
            var data = Data();
            return ((int)SHAppBarMessage(GetState, ref data) & AutoHideFlag) != 0;
        }
        set
        {
            var data = Data();
            data.Param = (IntPtr)(value ? AutoHideFlag : AlwaysOnTopFlag);
            SHAppBarMessage(SetState, ref data);
        }
    }
}
