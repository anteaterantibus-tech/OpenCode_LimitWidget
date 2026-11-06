using System.Runtime.InteropServices;

namespace OpenCodeWidget.Services;

public sealed record TaskbarInfo(
    IntPtr Handle,
    double Left,
    double Top,
    double Width,
    double Height);

public static class TaskbarLocator
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const int GwlpHwndParent = -8;
    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public static TaskbarInfo? GetPrimaryTaskbar()
    {
        var handle = FindWindow("Shell_TrayWnd", null);
        if (handle == IntPtr.Zero || !IsWindowVisible(handle))
            return null;

        if (!GetWindowRect(handle, out var rect))
            return null;

        var physicalWidth = rect.Right - rect.Left;
        var physicalHeight = rect.Bottom - rect.Top;
        if (physicalWidth <= 0 || physicalHeight <= 0 || physicalHeight >= physicalWidth)
            return null;

        var dpi = GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        return new TaskbarInfo(
            handle,
            rect.Left / scale,
            rect.Top / scale,
            physicalWidth / scale,
            physicalHeight / scale);
    }

    public static void KeepOnTop(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return;

        SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public static void SetOwner(IntPtr window, IntPtr owner)
    {
        if (window == IntPtr.Zero)
            return;

        SetWindowLongPtr(window, GwlpHwndParent, owner);
    }

    public static void ClampToWorkArea(IntPtr window)
    {
        if (window == IntPtr.Zero || !GetWindowRect(window, out var rect))
            return;

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var work = info.rcWork;
        var dx = 0;
        var dy = 0;

        if (rect.Right > work.Right)
            dx = work.Right - rect.Right;
        if (rect.Left + dx < work.Left)
            dx = work.Left - rect.Left;
        if (rect.Bottom > work.Bottom)
            dy = work.Bottom - rect.Bottom;
        if (rect.Top + dy < work.Top)
            dy = work.Top - rect.Top;

        if (dx == 0 && dy == 0)
            return;

        SetWindowPos(
            window,
            IntPtr.Zero,
            rect.Left + dx,
            rect.Top + dy,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }
}
