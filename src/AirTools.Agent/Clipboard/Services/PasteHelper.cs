using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace AirTools.Tools.Clipboard.Services;

public static class PasteHelper
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
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
        for (int i = 0; i < 30; i++)
        {
            if (ModifiersReleased()) break;
            if (i == 29) return "已复制，请松开修饰键后手动粘贴";
            await Task.Delay(20);
        }
        if (!SetForegroundWindow(target)) return "已复制，无法恢复目标窗口焦点，请手动粘贴";
        await Task.Delay(60);
        if (GetForegroundWindow() != target) return "已复制，目标窗口未获得焦点，请手动粘贴";
        var inputs = new[] { Key(0x11), Key(0x56), Key(0x56, true), Key(0x11, true) };
        uint sent = SendInput(4, inputs, Marshal.SizeOf<INPUT>());
        if (sent != 4)
        {
            SendInput(2, new[] { Key(0x56, true), Key(0x11, true) }, Marshal.SizeOf<INPUT>());
            return "已复制，系统阻止了粘贴输入；管理员窗口可能需要手动粘贴";
        }
        return null;
    }
    private static bool ModifiersReleased()
    {
        foreach (int key in new[] { 0x12, 0x10, 0x11, 0x5B, 0x5C }) if ((GetAsyncKeyState(key) & 0x8000) != 0) return false;
        return true;
    }
    private static INPUT Key(ushort key, bool up = false) => new() { Type = 1, Data = new INPUTUNION { Keyboard = new KEYBDINPUT { Key = key, Flags = up ? 2u : 0u } } };
    public static async void PasteToTarget(Window window, IntPtr target, Action copy)
    {
        var error = await PasteToTargetAsync(window, target, () => { copy(); return Task.FromResult(true); });
        if (error != null) MessageBox.Show(error, "AirTools");
    }
}
