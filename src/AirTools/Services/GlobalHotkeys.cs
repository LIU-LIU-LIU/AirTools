using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AirTools.Services;

public sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source = new(new HwndSourceParameters("AirTools.Hotkeys") { ParentWindow = new IntPtr(-3) });
    private Dictionary<string, (int Id, Action Action)> _active = new();
    private int _nextId = 100;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public GlobalHotkeys() => _source.AddHook(Hook);
    public void Apply(IEnumerable<(string Gesture, Action Action)> bindings)
    {
        var desired = new Dictionary<string, (uint Mods, uint Key, Action Action)>();
        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Gesture)) continue;
            var parsed = HotkeyParser.Parse(binding.Gesture);
            var key = $"{parsed.Modifiers}:{parsed.Key}";
            if (!desired.TryAdd(key, (parsed.Modifiers, parsed.Key, binding.Action))) throw new ArgumentException("快捷键重复：" + binding.Gesture);
        }
        var added = new Dictionary<string, (int Id, Action Action)>();
        try
        {
            foreach (var pair in desired)
            {
                if (_active.ContainsKey(pair.Key)) continue;
                var id = ++_nextId;
                if (!RegisterHotKey(_source.Handle, id, pair.Value.Mods, pair.Value.Key)) throw new Win32Exception(Marshal.GetLastWin32Error(), "快捷键已被其他程序占用");
                added.Add(pair.Key, (id, pair.Value.Action));
            }
        }
        catch { foreach (var item in added.Values) UnregisterHotKey(_source.Handle, item.Id); throw; }
        foreach (var pair in _active) if (!desired.ContainsKey(pair.Key)) UnregisterHotKey(_source.Handle, pair.Value.Id);
        var next = new Dictionary<string, (int Id, Action Action)>();
        foreach (var pair in desired) next[pair.Key] = (_active.TryGetValue(pair.Key, out var old) ? old.Id : added[pair.Key].Id, pair.Value.Action);
        _active = next;
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x312) foreach (var item in _active.Values) if (item.Id == wp.ToInt32()) { handled = true; item.Action(); break; }
        return IntPtr.Zero;
    }
    public void Dispose() { foreach (var item in _active.Values) UnregisterHotKey(_source.Handle, item.Id); _source.RemoveHook(Hook); _source.Dispose(); }
}
