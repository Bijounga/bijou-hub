using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BijouHub.Services;

// BijouHub's icon in the notification area ("hidden icons"), for running in the background like
// Discord: click to open, right-click for a menu. Talks to the shell directly (Shell_NotifyIcon)
// rather than pulling in WinForms for its NotifyIcon. Comes back by itself if Explorer restarts.
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 21; // WM_APP + n
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private readonly HwndSource _window;
    private readonly uint _taskbarCreated;
    private readonly IntPtr _icon;
    private string _tip = "BijouHub";
    private bool _shown;

    public event Action? Clicked;
    public event Action? MenuRequested;

    public TrayIcon()
    {
        // A hidden top-level window: it receives the icon's clicks, and can take the foreground so
        // the menu closes when you click elsewhere.
        _window = new HwndSource(new HwndSourceParameters("BijouHubTray") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) });
        _window.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadAppIcon();
    }

    public bool Visible
    {
        get => _shown;
        set
        {
            if (value == _shown) return;
            _shown = value;
            Notify(value ? NIM_ADD : NIM_DELETE, NIF_MESSAGE | NIF_ICON | NIF_TIP);
        }
    }

    // Hover text; the shell keeps 127 characters.
    public string ToolTip
    {
        set
        {
            var tip = value.Length > 127 ? value[..127] : value;
            if (tip == _tip) return;
            _tip = tip;
            if (_shown) Notify(NIM_MODIFY, NIF_TIP);
        }
    }

    public void ShowBalloon(string title, string text)
    {
        if (!_shown) return;
        var data = Data(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
        data.hBalloonIcon = _icon;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    // Call right before opening the menu, so it closes when the user clicks somewhere else.
    public void PrepareForMenu() => SetForegroundWindow(_window.Handle);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)lParam & 0xFFFF)
            {
                case WM_LBUTTONUP:
                    Clicked?.Invoke();
                    break;
                case WM_RBUTTONUP:
                    MenuRequested?.Invoke();
                    break;
            }
            handled = true;
        }
        else if (msg == _taskbarCreated && _shown)
        {
            Notify(NIM_ADD, NIF_MESSAGE | NIF_ICON | NIF_TIP); // Explorer restarted
        }
        return IntPtr.Zero;
    }

    private void Notify(int message, int flags)
    {
        var data = Data(flags);
        Shell_NotifyIcon(message, ref data);
    }

    private NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = ""
    };

    // The exe's own icon at the tray's size for this screen's scaling.
    private static IntPtr LoadAppIcon()
    {
        // The apphost keeps the app icon as resource 32512; SM_CXSMICON is the tray size at this DPI.
        var size = GetSystemMetrics(49 /* SM_CXSMICON */);
        var icon = LoadImage(GetModuleHandle(null), (IntPtr)32512, 1 /* IMAGE_ICON */, size, size, 0);
        if (icon != IntPtr.Zero) return icon;
        var small = new IntPtr[1];
        return ExtractIconEx(Environment.ProcessPath ?? "", 0, null, small, 1) > 0 ? small[0] : IntPtr.Zero;
    }

    public void Dispose()
    {
        Visible = false;
        _window.Dispose();
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
    }

    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const int NIIF_USER = 0x4, NIIF_LARGE_ICON = 0x20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, int type, int cx, int cy, int flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, int count);
}
