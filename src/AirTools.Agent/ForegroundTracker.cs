using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AirTools.Agent;

internal sealed class ForegroundTracker : IDisposable
{
    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private readonly WinEventDelegate _callback;
    private readonly IntPtr _hook;
    private IntPtr _lastExternal;
    public IntPtr Target { get; private set; }
    public bool PaletteActive { get; private set; }

    public ForegroundTracker()
    {
        _callback = (_, _, hwnd, _, _, _, _) => Observe(hwnd);
        Observe(GetForegroundWindow());
        _hook = SetWinEventHook(3, 3, IntPtr.Zero, _callback, 0, 0, 0);
    }
    private void Observe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            bool palette = process.ProcessName.StartsWith("Microsoft.CmdPal", StringComparison.OrdinalIgnoreCase);
            if (palette && !PaletteActive) Target = _lastExternal;
            PaletteActive = palette;
            if (!palette && pid != Environment.ProcessId) _lastExternal = hwnd;
        }
        catch { PaletteActive = false; }
    }
    public void Dispose() { if (_hook != IntPtr.Zero) UnhookWinEvent(_hook); }
}
