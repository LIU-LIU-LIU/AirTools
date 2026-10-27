using System.Text.Json;
using AirTools.Core;

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
    public string[] Arguments { get; set; } = [];
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
    public List<LaunchTask> Tasks { get; set; } = [];
    public static string DataDirectory => AgentProtocol.DataDirectory;
    public static string SettingsPath => Path.Combine(DataDirectory, "launcher.json");
    public static LauncherSettings Load()
    {
        var settings = File.Exists(SettingsPath)
        ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsPath)) ?? throw new IOException("任务配置无效")
        : new LauncherSettings { Tasks = [
            new() { Name = "Python VPN 监控", Alias = "vpn python", Kind = LaunchKind.Terminal, Executable = "python.exe", Arguments = [@".\adb_vpn_monitor.py"], WorkingDirectory = @"D:\Users\DELL\Desktop\android-work\QuasarVPN" },
            new() { Name = "Wireshark 远程抓包", Alias = "ssh wireshark", Kind = LaunchKind.SshCapture, Executable = @"C:\Program Files\Wireshark\Wireshark.exe" }
        ] };
        var ids = new HashSet<Guid>();
        var changed = false;
        foreach (var task in settings.Tasks)
            if (!Guid.TryParse(task.Id, out var id) || !ids.Add(id))
            {
                task.Id = Guid.NewGuid().ToString("N");
                ids.Add(Guid.Parse(task.Id)); changed = true;
            }
        if (changed)
        {
            var backup = SettingsPath + ".id-migration-backup";
            if (!File.Exists(backup)) File.Copy(SettingsPath, backup, overwrite: false);
            settings.Save();
        }
        return settings;
    }
    public void Save() => AtomicFile.Write(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    public static void Validate(LaunchTask task)
    {
        if (!Guid.TryParse(task.Id, out _)) throw new ArgumentException("任务 ID 无效");
        if (string.IsNullOrWhiteSpace(task.Name)) throw new ArgumentException("请填写任务名称");
        if (!Enum.IsDefined(task.Kind) || !Enum.IsDefined(task.TerminalPolicy)) throw new ArgumentException("任务类型或终端策略无效");
        if (task.Kind == LaunchKind.PowerShell && string.IsNullOrWhiteSpace(task.Script)) throw new ArgumentException("请填写脚本");
        if (task.Kind != LaunchKind.PowerShell && string.IsNullOrWhiteSpace(task.Executable)) throw new ArgumentException("请填写程序路径");
        if (task.Kind == LaunchKind.SshCapture && (string.IsNullOrWhiteSpace(task.Host) || task.Port is < 1 or > 65535)) throw new ArgumentException("SSH 主机或端口无效");
        task.Arguments ??= [];
    }
}

public static class AtomicFile
{
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class LegacyImport
{
    public static void Import(string source, string destination)
    {
        var marker = Path.Combine(destination, ".imported");
        if (File.Exists(marker)) return;
        Directory.CreateDirectory(destination);
        foreach (var relative in new[] { "launcher.json", Path.Combine("Clipboard", "history.json") })
        {
            var from = Path.Combine(source, relative);
            var to = Path.Combine(destination, relative);
            if (!File.Exists(from) || File.Exists(to)) continue;
            var json = File.ReadAllText(from);
            using var validation = JsonDocument.Parse(json);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to + ".import-backup", false);
            AtomicFile.Write(to, json);
        }
        AtomicFile.Write(marker, DateTimeOffset.UtcNow.ToString("O"));
    }
}
