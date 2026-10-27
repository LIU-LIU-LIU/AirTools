using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace AirTools.Services;

public enum LaunchKind { Program, Terminal, PowerShell, SshCapture }
public enum TerminalPolicy { Close, KeepOnFailure, Keep }
public sealed class LaunchTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新任务";
    public string Alias { get; set; } = "";
    public string Icon { get; set; } = "▶";
    public LaunchKind Kind { get; set; }
    public string Executable { get; set; } = "";
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public string WorkingDirectory { get; set; } = "";
    public string Script { get; set; } = "";
    public TerminalPolicy TerminalPolicy { get; set; }
    public string Hotkey { get; set; } = "";
    public string Host { get; set; } = "192.168.30.13";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    public string EncryptedPassword { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Interface { get; set; } = "any";
    public string Tcpdump { get; set; } = "tcpdump";
    public string CaptureFilter { get; set; } = "not tcp port 22";
    public string ExtraCaptureArguments { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class LauncherSettings
{
    public string Hotkey { get; set; } = "Ctrl+Space";
    public string ClipboardHotkey { get; set; } = "Ctrl+Shift+V";
    public string ClipboardModeKey { get; set; } = "c";
    public string FileModeKey { get; set; } = "f";
    public List<string> Directories { get; set; } = new() { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
    public List<string> Favorites { get; set; } = new();
    public List<string> RecentFiles { get; set; } = new();
    public List<LaunchTask> Tasks { get; set; } = new();
    public static string DataDirectory => Environment.GetEnvironmentVariable("AIRTOOLS_DATA_DIRECTORY") is string path && path.Length > 0 ? Path.GetFullPath(path) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirTools");
    public static string SettingsPath => Path.Combine(DataDirectory, "launcher.json");
    public static LauncherSettings Load()
    {
        if (File.Exists(SettingsPath))
        {
            try { return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
            catch (Exception ex) { System.Windows.MessageBox.Show("启动器配置无法读取，原文件不会被覆盖：\n" + ex.Message); throw; }
        }
        var legacy = Tools.Clipboard.Models.ClipboardAppSettings.Load();
        return new LauncherSettings {
            ClipboardHotkey = legacy.GetHotkeyDisplay(),
            Tasks = new() {
                new() { Name = "Python VPN 监控", Alias = "vpn python", Kind = LaunchKind.Terminal, Executable = "python.exe", Arguments = new[] { @".\adb_vpn_monitor.py" }, WorkingDirectory = @"D:\Users\DELL\Desktop\android-work\QuasarVPN" },
                new() { Name = "Wireshark 远程抓包", Alias = "ssh wireshark", Kind = LaunchKind.SshCapture, Executable = @"C:\Program Files\Wireshark\Wireshark.exe", Icon = "◉" }
            }
        };
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, SettingsPath, true);
    }
    public LauncherSettings Clone() => JsonSerializer.Deserialize<LauncherSettings>(JsonSerializer.Serialize(this))!;
}

public static class HotkeyParser
{
    public static (uint Modifiers, uint Key) Parse(string text)
    {
        var gesture = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(text)!;
        if (gesture.Modifiers == ModifierKeys.None) throw new ArgumentException("快捷键至少需要一个修饰键");
        uint mods = 0x4000;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= 1;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 2;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= 4;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= 8;
        return (mods, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key));
    }
}
