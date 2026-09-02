using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace BijouHub.Services;

// Pulls a file/folder's shell icon via SHGetFileInfo + CreateBitmapSourceFromHIcon
// (pure Win32/WPF interop) instead of System.Drawing, which this environment has
// had assembly-resolution trouble with in ad-hoc scripts.
public static class IconExtractor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static BitmapSource? GetIcon(string path)
    {
        try
        {
            var exists = File.Exists(path) || Directory.Exists(path);
            var flags = SHGFI_ICON | SHGFI_LARGEICON;
            if (!exists) flags |= SHGFI_USEFILEATTRIBUTES;

            var shfi = new SHFILEINFO();
            var result = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;

            try
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }
}
