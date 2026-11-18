using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace AirTools.Tools.Clipboard.Services;

/// <summary>Writes plain Unicode text without WPF/OLE re-entrancy.</summary>
internal sealed class NativeTextClipboard : IDisposable
{
    private HwndSource? _owner;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);

    public void Write(string text)
    {
        _owner ??= new HwndSource(new HwndSourceParameters("AirTools.TextClipboard") { ParentWindow = new IntPtr(-3) });
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = GlobalAlloc(0x0002, (nuint)bytes.Length);
        if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            if (!OpenClipboard(_owner.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error(), "系统剪切板正被其他程序占用");
            try
            {
                if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error(), "写入系统剪切板失败");
                if (SetClipboardData(13, memory) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "写入系统剪切板失败");
                memory = IntPtr.Zero;
            }
            finally { CloseClipboard(); }
        }
        finally { if (memory != IntPtr.Zero) GlobalFree(memory); }
    }

    public void Dispose() => _owner?.Dispose();
}
