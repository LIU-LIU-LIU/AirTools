using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Renci.SshNet;

namespace AirTools.Services;

public interface ITaskRunner { Task RunAsync(LaunchTask task); void Stop(string id); }

public sealed class TaskRunner : ITaskRunner, IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    public event Action<string>? StatusChanged;
    public Action? SaveCredentials { get; set; }
    public static string Protect(string password) => password.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string value) => value.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    public static string QuotePs(string value) => "'" + value.Replace("'", "''") + "'";
    public static string QuoteSh(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
    public static string BuildInvocation(LaunchTask task) => task.Kind == LaunchKind.PowerShell ? task.Script : "& " + QuotePs(task.Executable) + " " + string.Join(" ", task.Arguments.Select(QuotePs));
    public static string LogPath(string id) => Path.Combine(LauncherSettings.DataDirectory, "Logs", id + ".log");
    public static string ResultPath(string id) => Path.Combine(LauncherSettings.DataDirectory, "Logs", id + ".exit");

    public async Task RunAsync(LaunchTask task)
    {
        var cancel = new CancellationTokenSource();
        if (!_running.TryAdd(task.Id, cancel)) { cancel.Dispose(); StatusChanged?.Invoke(task.Name + " 已在运行"); return; }
        try
        {
            if (task.Kind == LaunchKind.SshCapture) await CaptureAsync(task, cancel.Token);
            else if (task.Kind == LaunchKind.Program)
            {
                var info = CreateProcess(task);
                using var process = Process.Start(info) ?? throw new IOException("无法启动程序");
                StatusChanged?.Invoke("已启动：" + task.Name);
            }
            else await RunTerminalAsync(task, cancel.Token);
        }
        catch (OperationCanceledException) { StatusChanged?.Invoke(task.Name + " 已停止"); }
        catch (Exception ex) { StatusChanged?.Invoke(task.Name + "：" + ex.Message); await Application.Current.Dispatcher.InvokeAsync(() => MessageBox.Show(ex.Message, task.Name, MessageBoxButton.OK, MessageBoxImage.Warning)); }
        finally { _running.TryRemove(task.Id, out _); cancel.Dispose(); }
    }
    private static ProcessStartInfo CreateProcess(LaunchTask task)
    {
        if (string.IsNullOrWhiteSpace(task.Executable)) throw new ArgumentException("请配置程序路径");
        if (!string.IsNullOrWhiteSpace(task.WorkingDirectory) && !Directory.Exists(task.WorkingDirectory)) throw new DirectoryNotFoundException("工作目录不存在：" + task.WorkingDirectory);
        var info = new ProcessStartInfo(task.Executable) { UseShellExecute = false, WorkingDirectory = task.WorkingDirectory };
        foreach (var argument in task.Arguments) info.ArgumentList.Add(argument);
        return info;
    }
    private async Task RunTerminalAsync(LaunchTask task, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(task.WorkingDirectory) && !Directory.Exists(task.WorkingDirectory)) throw new DirectoryNotFoundException("工作目录不存在：" + task.WorkingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath(task.Id))!);
        var script = BuildTerminalScript(task);
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = task.WorkingDirectory };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-EncodedCommand"); info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(info) ?? throw new IOException("无法启动终端");
        StatusChanged?.Invoke("正在运行：" + task.Name);
        // The visible terminal belongs to the user. App exit detaches, rather than terminating their script.
        await process.WaitForExitAsync(token);
        StatusChanged?.Invoke(task.Name + " · 退出码 " + process.ExitCode);
    }
    public static string BuildTerminalScript(LaunchTask task)
    {
        var invocation = BuildInvocation(task);
        // Keep a bounded ring of output in the child shell so logging never grows unbounded.
        // Windows PowerShell decodes native pipes with Console.OutputEncoding. Python
        // must emit the same encoding. Native stderr is output, not proof of failure.
        var script = "$utf8=New-Object System.Text.UTF8Encoding($false); [Console]::InputEncoding=$utf8; [Console]::OutputEncoding=$utf8; $OutputEncoding=$utf8; $env:PYTHONUTF8='1'; $env:PYTHONIOENCODING='utf-8'; $env:PYTHONUNBUFFERED='1'; $ErrorActionPreference='Continue'; $global:LASTEXITCODE=0; $code=0; $lines=New-Object 'System.Collections.Generic.Queue[string]'; try { ";
        if (task.Kind != LaunchKind.PowerShell) script += "$null=Get-Command " + QuotePs(task.Executable) + " -ErrorAction Stop; ";
        script += "& { " + (task.Kind == LaunchKind.PowerShell ? "$ErrorActionPreference='Stop'; " : "") + invocation + " } 2>&1 | ForEach-Object { $s=[string]$_; Write-Host $s; $lines.Enqueue($s.Substring(0,[Math]::Min($s.Length,4096))); while($lines.Count -gt 256){[void]$lines.Dequeue()} }; if($null -ne $LASTEXITCODE){$code=$LASTEXITCODE} } catch { Write-Host $_ -ForegroundColor Red; $lines.Enqueue([string]$_); $code=1 }; [IO.File]::WriteAllLines(" + QuotePs(LogPath(task.Id)) + ",$lines.ToArray(),$utf8); [IO.File]::WriteAllText(" + QuotePs(ResultPath(task.Id)) + ",[string]$code); ";
        if (task.TerminalPolicy == TerminalPolicy.Keep) script += "Read-Host '运行结束，按 Enter 关闭'; ";
        else if (task.TerminalPolicy == TerminalPolicy.KeepOnFailure) script += "if($code -ne 0){Read-Host '运行失败，按 Enter 关闭'}; ";
        script += "exit $code";
        return script;
    }

    private SshClient CreateSsh(LaunchTask task)
    {
        if (string.IsNullOrWhiteSpace(task.Host) || task.Port is < 1 or > 65535) throw new ArgumentException("SSH 主机或端口无效");
        var password = Unprotect(task.EncryptedPassword);
        var auth = string.IsNullOrWhiteSpace(task.PrivateKey)
            ? (AuthenticationMethod)new PasswordAuthenticationMethod(task.Username, password)
            : new PrivateKeyAuthenticationMethod(task.Username, new PrivateKeyFile(task.PrivateKey, password));
        if (string.IsNullOrWhiteSpace(task.PrivateKey) && password.Length == 0) throw new ArgumentException("请先编辑任务，填写 SSH 密码或私钥");
        var client = new SshClient(new ConnectionInfo(task.Host, task.Port, task.Username, auth) { Timeout = TimeSpan.FromSeconds(15) });
        client.KeepAliveInterval = TimeSpan.FromSeconds(15);
        client.HostKeyReceived += (_, e) =>
        {
            var fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=');
            if (task.Fingerprint.Length > 0) { e.CanTrust = task.Fingerprint == fingerprint; return; }
            e.CanTrust = Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"首次连接 {task.Host}:{task.Port}\n\n主机指纹：{fingerprint}\n\n确认信任此主机？", "SSH 主机验证", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
            if (e.CanTrust) Application.Current.Dispatcher.Invoke(() => { task.Fingerprint = fingerprint; SaveCredentials?.Invoke(); });
        };
        return client;
    }
    public async Task TestAsync(LaunchTask task)
    {
        await Task.Run(() => { using var ssh = CreateSsh(task); ssh.Connect(); using var command = ssh.CreateCommand(QuoteSh(task.Tcpdump) + " --version"); command.CommandTimeout = TimeSpan.FromSeconds(10); var result = command.Execute(); if (command.ExitStatus != 0) throw new IOException(command.Error); });
    }
    private async Task CaptureAsync(LaunchTask task, CancellationToken token)
    {
        if (!File.Exists(task.Executable)) throw new FileNotFoundException("找不到 Wireshark，请编辑程序路径", task.Executable);
        using var ssh = CreateSsh(task);
        await Task.Run(() => ssh.Connect(), token);
        token.ThrowIfCancellationRequested();
        var name = "AirTools-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var info = new ProcessStartInfo(task.Executable) { UseShellExecute = false };
        info.ArgumentList.Add("-k"); info.ArgumentList.Add("-i"); info.ArgumentList.Add(@"\\.\pipe\" + name);
        using var wireshark = Process.Start(info) ?? throw new IOException("Wireshark 启动失败");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        var exit = wireshark.WaitForExitAsync(linked.Token);
        _ = exit.ContinueWith(_ => { try { linked.Cancel(); } catch (ObjectDisposedException) { } }, TaskScheduler.Default);
        using var registration = linked.Token.Register(() => { try { ssh.Disconnect(); } catch { } });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await pipe.WaitForConnectionAsync(timeout.Token);
        string capture = QuoteSh(task.Tcpdump) + " -U -n -s 0 -i " + QuoteSh(task.Interface) + " -w - " + task.ExtraCaptureArguments + (task.CaptureFilter.Length > 0 ? " " + QuoteSh(task.CaptureFilter) : "");
        using var command = ssh.CreateCommand(capture);
        command.CommandTimeout = TimeSpan.FromDays(7);
        var pending = command.BeginExecute();
        StatusChanged?.Invoke("正在抓包：" + task.Host + " / " + task.Interface);
        try
        {
            // Drain stderr concurrently; cap retained text even during long captures.
            var stderr = Task.Run(async () => { char[] buffer = new char[1024]; string last = ""; using var reader = new StreamReader(command.ExtendedOutputStream); int n; while ((n = await reader.ReadAsync(buffer, linked.Token)) > 0) { last += new string(buffer, 0, n); if (last.Length > 4096) last = last[^4096..]; } return last; }, linked.Token);
            await command.OutputStream.CopyToAsync(pipe, linked.Token);
            command.EndExecute(pending);
            var error = await stderr;
            if (command.ExitStatus != 0) throw new IOException("远端抓包失败：" + error);
        }
        finally { linked.Cancel(); if (ssh.IsConnected) ssh.Disconnect(); }
    }
    public void Stop(string id) { if (_running.TryGetValue(id, out var cancel)) cancel.Cancel(); }
    public void Dispose() { foreach (var cancel in _running.Values) cancel.Cancel(); }
}
