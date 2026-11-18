using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AirTools.Tools.Clipboard.Services;

public static class PasteHelper
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp, uint flags, uint timeout, out UIntPtr result);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct GUITHREADINFO { public uint Size, Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public RECT CaretRect; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT Keyboard; [FieldOffset(0)] public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr Extra; }
    public static IntPtr GetPreviousForegroundWindow() => GetForegroundWindow();
    public static void RestoreForeground(IntPtr target) { if (target != IntPtr.Zero && IsWindow(target)) SetForegroundWindow(target); }
    public static async Task<string?> PasteToTargetAsync(Window window, IntPtr target, Func<Task<bool>> copy)
    {
        if (!await copy()) return "复制失败，剪切板可能正被其他程序占用";
        window.Hide();
        if (target == IntPtr.Zero || !IsWindow(target)) return "已复制，原目标窗口已关闭或不可用";
        if (!SetForegroundWindow(target)) return "已复制，无法恢复目标窗口焦点，请手动粘贴";
        await Task.Delay(60);
        return await PasteFocusedAsync(target);
    }

    /// <summary>Rack path: never activates or restores another window.</summary>
    public static async Task<string?> PasteWithoutActivationAsync(IntPtr target, Func<Task<bool>> copy, bool compatibilityFallback = true)
    {
        // Defer past MouseUp processing / capture release on a non-activating surface.
        await Task.Delay(70);
        if (GetForegroundWindow() != target) return "目标焦点已变化，本次未粘贴";
        if (!await copy()) return "复制失败，剪切板可能正被其他程序占用";
        return await PasteFocusedAsync(target, compatibilityFallback);
    }

    private static async Task<string?> PasteFocusedAsync(IntPtr target, bool compatibilityFallback = true)
    {
        if (target == IntPtr.Zero || !IsWindow(target)) return "已复制，原目标窗口已关闭或不可用";
        GetWindowThreadProcessId(target, out uint processId);
        if (processId == Environment.ProcessId) return "已复制，请先在其他应用中放置输入光标";
        for (int i = 0; i < 30; i++)
        {
            if (ModifiersReleased()) break;
            if (i == 29) return "已复制，请松开修饰键后手动粘贴";
            await Task.Delay(20);
        }
        if (GetForegroundWindow() != target) return "已复制，目标焦点已变化，本次未自动粘贴";
        var info = new GUITHREADINFO { Size = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        uint threadId = GetWindowThreadProcessId(target, out _);
        if (GetGUIThreadInfo(threadId, ref info) && info.Focus != IntPtr.Zero)
        {
            var name = new StringBuilder(256); GetClassName(info.Focus, name, name.Capacity);
            PasteDiagnostics.Write($"target={target:X}, focus={info.Focus:X}, class={name}");
            // A delivered WM_PASTE does not prove that an arbitrary host handled it.
            // Only standard Edit controls document this message. Never fall back
            // after delivery to such a control, which could paste the text twice.
            if (SupportsNativePaste(name.ToString()))
            {
                var sentMessage = SendMessageTimeout(info.Focus, 0x302, IntPtr.Zero, IntPtr.Zero, 0x22, 500, out _);
                PasteDiagnostics.Write($"WM_PASTE delivery={sentMessage != IntPtr.Zero}, win32={Marshal.GetLastWin32Error()}");
                if (sentMessage != IntPtr.Zero) return null;
                return "已复制，原生粘贴消息被阻止或超时，请手动粘贴";
            }
        }
        if (!compatibilityFallback) return "已复制，此应用未提供标准粘贴控件，请手动粘贴或启用兼容模式";
        var inputs = new[] { Key(0x10), Key(0x2D), Key(0x2D, true), Key(0x10, true) };
        uint sent = SendInput(4, inputs, Marshal.SizeOf<INPUT>());
        PasteDiagnostics.Write($"Shift+Insert sent={sent}/4, inputSize={Marshal.SizeOf<INPUT>()}, win32={Marshal.GetLastWin32Error()}");
        if (sent != 4)
        {
            SendInput(2, new[] { Key(0x2D, true), Key(0x10, true) }, Marshal.SizeOf<INPUT>());
            return "已复制，系统阻止了粘贴输入；管理员窗口可能需要手动粘贴";
        }
        return null;
    }
    public static bool SupportsNativePaste(string className) => className.Equals("Edit", StringComparison.OrdinalIgnoreCase)
        || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
        || className.StartsWith("RICHEDIT", StringComparison.OrdinalIgnoreCase)
        || className.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase);
    private static bool ModifiersReleased()
    {
        foreach (int key in new[] { 0x12, 0x10, 0x11, 0x5B, 0x5C }) if ((GetAsyncKeyState(key) & 0x8000) != 0) return false;
        return true;
    }
    private static INPUT Key(ushort key, bool up = false) => new() { Type = 1, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { Key = key, Flags = (up ? 2u : 0u) | (key == 0x2D ? 1u : 0u) } } };
    public static async void PasteToTarget(Window window, IntPtr target, Action copy)
    {
        var error = await PasteToTargetAsync(window, target, () => { copy(); return Task.FromResult(true); });
        if (error != null) MessageBox.Show(error, "AirTools");
    }
}
