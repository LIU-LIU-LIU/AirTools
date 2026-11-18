using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AirTools.Tools.Clipboard.Services;

/// <summary>Windows adapter: mouse interaction must leave the target app active.</summary>
public static class NonActivatingWindow
{
    public const int RequiredStyles = 0x08000000 | 0x80; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    public static int GetStyles(Window window) => GetWindowLong(new WindowInteropHelper(window).Handle, -20);
    public static void Apply(Window window)
    {
        window.ShowActivated = false;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | RequiredStyles);
        HwndSource.FromHwnd(handle)?.AddHook(Hook);
    }
    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        return IntPtr.Zero;
    }
}
