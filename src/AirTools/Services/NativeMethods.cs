using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AirTools.Services;
internal static class NativeMethods
{
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    public static void PositionLauncher(Window window, IntPtr target, Rectangle area)
    {
        double scale = 1;
        try { if (GetDpiForMonitor(MonitorFromWindow(target, 2), 0, out var x, out _) == 0) scale = x / 96.0; } catch (DllNotFoundException) { }
        int width = Math.Min((int)(window.Width * scale), area.Width - 24);
        int height = Math.Min((int)(window.Height * scale), area.Height - 24);
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, area.Left + (area.Width - width) / 2, area.Top + Math.Min((int)(area.Height * .18), area.Height - height), width, height, 0x14);
    }
}
