using System.Runtime.InteropServices;

namespace RenderMusicPro.Services;

/// <summary>Nhấp nháy nút taskbar (FlashWindowEx) khi cửa sổ không phải cửa sổ đang dùng.</summary>
internal static class TaskbarFlash
{
    private const uint FlashAll = 3;        // FLASHW_ALL: caption + taskbar
    private const uint FlashUntilFocus = 12; // FLASHW_TIMERNOFG: nháy đến khi cửa sổ được kích hoạt

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public static void FlashIfInactive(IntPtr handle)
    {
        try
        {
            if (handle == IntPtr.Zero || GetForegroundWindow() == handle) return;
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = handle,
                dwFlags = FlashAll | FlashUntilFocus,
                uCount = uint.MaxValue,
                dwTimeout = 0
            };
            FlashWindowEx(ref info);
        }
        catch
        {
            // Không nháy được taskbar - bỏ qua.
        }
    }
}
