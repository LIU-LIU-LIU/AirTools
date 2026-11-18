using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AirTools.Core;
using AirTools.Tools.Clipboard.Models;
using AirTools.Tools.Clipboard.Services;

namespace AirTools.Services;

/// <summary>
/// Local, current-user-only pipe that lets the Command Palette extension search clipboard history and run
/// saved tasks. The extension never touches files or credentials: AppHost stays the single owner of state.
/// </summary>
public sealed class PipeServer : IDisposable
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private readonly AppHost _host;
    private readonly CancellationTokenSource _stop = new();
    public PipeServer(AppHost host) { _host = host; _ = ServeAsync(); }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(AgentProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await AgentProtocol.ReadAsync<AgentRequest>(pipe, timeout.Token).ConfigureAwait(false);
                AgentReply reply;
                try { reply = await (await Application.Current.Dispatcher.InvokeAsync(() => HandleAsync(request))).ConfigureAwait(false); }
                catch (Exception ex) { reply = new(false, ex.Message); }
                await AgentProtocol.WriteAsync(pipe, reply, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("pipe: " + ex.Message); await Task.Delay(200).ConfigureAwait(false); }
        }
    }

    private static bool IsLink(ClipboardItem item) => item.ItemType == ClipboardItemType.Text && ClipboardLink.TryParse(item.Text, out _);
    private static string KindName(LaunchKind kind) => kind switch { LaunchKind.Program => "程序", LaunchKind.Terminal => "终端命令", LaunchKind.PowerShell => "PowerShell 脚本", _ => "SSH 远程抓包" };

    private async Task<AgentReply> HandleAsync(AgentRequest request)
    {
        var clipboard = _host.Clipboard;
        var item = clipboard.Items.FirstOrDefault(x => x.Id == request.Id);
        switch (request.Operation)
        {
            case "status": return new(true, "AirTools 独立版正在运行");
            case "clipboard.list":
                var entries = clipboard.Search(request.Query)
                    .Where(x => request.Data is "" or "all" || request.Data == "pinned" && x.IsPinned || x.ItemType.ToString() == request.Data || request.Data == "Link" && IsLink(x))
                    .OrderByDescending(x => x.IsPinned).Take(50)
                    .Select(x => new ClipboardEntry(x.Id, x.Text.Length > 4000 ? x.Text[..4000] : x.Text, IsLink(x) ? "Link" : x.ItemType.ToString(), x.IsPinned, x.CopyTime)).ToArray();
                return new(true, clipboard.Paused ? "记录已暂停" : "", JsonSerializer.Serialize(entries));
            case "clipboard.open":
                if (item == null || !IsLink(item) || !ClipboardLink.TryParse(item.Text, out var uri)) return new(false, "请选择完整的协议链接");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return new(true);
            case "clipboard.copy":
            case "clipboard.paste":
                if (item == null) return new(false, "历史项已不存在，请刷新");
                if (!await clipboard.CopyToClipboardAsync(item)) return new(false, clipboard.LastError ?? "复制失败");
                if (request.Operation == "clipboard.paste") _ = PasteAfterPaletteClosesAsync();
                return new(true, "已复制");
            case "clipboard.pin": if (item == null) return new(false, "历史项已不存在"); clipboard.TogglePin(item); return new(true);
            case "clipboard.delete": if (item != null) clipboard.DeleteItem(item); return new(true);
            case "clipboard.clear": clipboard.ClearUnpinned(); return new(true);
            case "clipboard.preview":
                if (item?.ItemType != ClipboardItemType.Image) return new(false, "请选择图片");
                var bitmap = clipboard.GetImage(item);
                if (bitmap != null) { var viewer = new AirTools.Tools.Clipboard.ImageViewerWindow(bitmap); viewer.Show(); viewer.Activate(); }
                return new(true);
            case "tasks.list":
                var tasks = _host.Settings.Tasks.Select(t => new TaskInfo(t.Id, t.Name, t.Alias, KindName(t.Kind), t.Icon, _host.Runner.IsRunning(t.Id))).ToArray();
                return new(true, "", JsonSerializer.Serialize(tasks));
            case "tasks.run":
                var task = _host.Settings.Tasks.FirstOrDefault(x => x.Id == request.Id) ?? throw new ArgumentException("任务不存在");
                _ = _host.Runner.RunAsync(task); return new(true, "已启动：" + task.Name);
            case "tasks.stop": return _host.Runner.Stop(request.Id) ? new(true, "已结束") : new(true, "当前没有在运行");
            case "tasks.log":
                var path = TaskRunner.LogPath(request.Id);
                var text = File.Exists(path) ? File.ReadAllText(path) : "尚无执行日志";
                return new(true, "", text.Length > 65536 ? text[^65536..] : text);
            case "ui.settings": _host.ShowSettings(string.IsNullOrEmpty(request.Data) ? "主页" : request.Data); return new(true);
            case "ui.search": _host.ShowSearch(); return new(true);
            default: return new(false, "不支持的操作");
        }
    }

    /// <summary>Waits for the palette to hand focus back, then pastes once. If focus never returns to another app, the text stays copied.</summary>
    private async Task PasteAfterPaletteClosesAsync()
    {
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(50);
            var target = GetForegroundWindow();
            if (target == IntPtr.Zero) continue;
            GetWindowThreadProcessId(target, out uint pid);
            if (pid == Environment.ProcessId) return;
            string name; try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); name = p.ProcessName; } catch { continue; }
            if (name.StartsWith("Microsoft.CmdPal", StringComparison.OrdinalIgnoreCase)) continue;
            await Task.Delay(80);
            if (GetForegroundWindow() != target) return;
            await PasteHelper.PasteWithoutActivationAsync(target, () => Task.FromResult(true), _host.Settings.PasteCompatibilityFallback);
            return;
        }
    }

    public void Dispose() => _stop.Cancel();
}
