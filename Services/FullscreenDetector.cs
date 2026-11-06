using System.Runtime.InteropServices;
using System.Text;

namespace OpenCodeWidget.Services;

public static class FullscreenDetector
{
    private const int MonitorDefaultToNearest = 2;
    private const int Tolerance = 4;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "TaskListThumbnailWnd",
        "DV2ControlHost",
    };

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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

    public static bool IsFullscreenOnSameMonitor(IntPtr ourWindow)
    {
        if (ourWindow == IntPtr.Zero)
            return false;

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == ourWindow)
            return false;

        if (!IsWindowVisible(foreground) || IsIconic(foreground))
            return false;

        GetWindowThreadProcessId(foreground, out var processId);
        if (processId == (uint)Environment.ProcessId)
            return false;

        var className = new StringBuilder(256);
        if (GetClassName(foreground, className, className.Capacity) > 0 &&
            ShellClasses.Contains(className.ToString()))
        {
            return false;
        }

        if (!GetWindowRect(foreground, out var windowRect))
            return false;

        var foregroundMonitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var ourMonitor = MonitorFromWindow(ourWindow, MonitorDefaultToNearest);

        if (foregroundMonitor == IntPtr.Zero ||
            ourMonitor == IntPtr.Zero ||
            foregroundMonitor != ourMonitor)
        {
            return false;
        }

        var monitorInfo = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>(),
        };

        if (!GetMonitorInfo(foregroundMonitor, ref monitorInfo))
            return false;

        var monitor = monitorInfo.rcMonitor;

        return windowRect.Left <= monitor.Left + Tolerance &&
               windowRect.Top <= monitor.Top + Tolerance &&
               windowRect.Right >= monitor.Right - Tolerance &&
               windowRect.Bottom >= monitor.Bottom - Tolerance;
    }
}
