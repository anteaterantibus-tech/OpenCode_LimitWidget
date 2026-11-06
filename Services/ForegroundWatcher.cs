using System.Runtime.InteropServices;

namespace OpenCodeWidget.Services;

public sealed class ForegroundWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private readonly WinEventDelegate _callback;
    private IntPtr _hook;

    public ForegroundWatcher()
    {
        _callback = OnEvent;
    }

    public event Action? ForegroundChanged;

    public void Start()
    {
        if (_hook != IntPtr.Zero)
            return;

        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
    }

    private void OnEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        ForegroundChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
            return;

        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
