using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AirTools.Core;
using AirTools.Services;
using AirTools.Tools.Clipboard;
using AirTools.Tools.Clipboard.Models;
using AirTools.Tools.Clipboard.Services;

namespace AirTools.Agent;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "Local\\" + AgentProtocol.PipeName, out bool owned);
        if (!owned) return;
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirTools");
            LegacyImport.Import(old, AgentProtocol.DataDirectory);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var host = new AgentHost(app);
            app.Run();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "AirTools 后台启动失败"); }
        finally { mutex.ReleaseMutex(); }
    }
}

internal sealed class AgentHost : IDisposable
{
    private readonly Application _app;
    private readonly ClipboardManagerService _clipboard = new();
    private readonly ClipboardMonitor _monitor = new();
    private readonly TaskRunner _runner = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Window _window = new() { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
    private readonly ForegroundTracker _foreground = new();
    private readonly LauncherSettings _tasks = LauncherSettings.Load();
    private AgentOptions _options = new();
    private string _status = "就绪";

    public AgentHost(Application app)
    {
        _app = app;
        var optionsFile = Path.Combine(AgentProtocol.DataDirectory, "agent.json");
        if (File.Exists(optionsFile)) _options = JsonSerializer.Deserialize<AgentOptions>(File.ReadAllText(optionsFile)) ?? new();
        _clipboard.MaxHistoryCount = _options.MaxHistory;
        _monitor.Start(_window);
        new WindowInteropHelper(_window).EnsureHandle();
        _monitor.ClipboardChanged += (_, _) => { if (_options.RecordClipboard) { _clipboard.HandleClipboardChange(); if (_clipboard.LastError is { } error) _status = error; } };
        _runner.StatusChanged += text => _status = text;
        _runner.SaveCredentials = _tasks.Save;
        _ = ServeAsync();
    }

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
                try { reply = await (await _app.Dispatcher.InvokeAsync(() => HandleAsync(request))).ConfigureAwait(false); }
                catch (Exception ex) { reply = new(false, ex.Message); }
                await AgentProtocol.WriteAsync(pipe, reply, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) { _status = "后台通信：" + ex.Message; }
        }
    }

    private async Task<AgentReply> HandleAsync(AgentRequest request)
    {
        var item = _clipboard.Items.FirstOrDefault(x => x.Id == request.Id);
        switch (request.Operation)
        {
            case "status": return new(true, _status, JsonSerializer.Serialize(_options));
            case "clipboard.list":
                var thumbnailBudget = new[] { 3_400_000 };
                var entries = _clipboard.Items.Select(x => (Item: x, Score: SearchRanking.Score(x.Text, request.Query)))
                    .Where(x => x.Score > 0).OrderByDescending(x => x.Item.IsPinned).ThenByDescending(x => x.Score).ThenByDescending(x => x.Item.CopyTime).Select(x => x.Item)
                    .Where(x => request.Data is "" or "all" || request.Data == "pinned" && x.IsPinned || x.ItemType.ToString() == request.Data || request.Data == "Link" && IsLink(x))
                    .Take(50).Select(x => new ClipboardEntry(x.Id, x.Text.Length > 4000 ? x.Text[..4000] : x.Text, IsLink(x) ? "Link" : x.ItemType.ToString(), x.IsPinned, x.CopyTime, Thumbnail(x, thumbnailBudget))).ToArray();
                return new(true, _status, JsonSerializer.Serialize(entries));
            case "clipboard.open":
                if (item == null || !IsLink(item) || !ClipboardLink.TryParse(item.Text, out var uri)) return new(false, "请选择完整的协议连接");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return new(true, "已交给 Windows 默认应用打开");
            case "clipboard.copy":
            case "clipboard.paste":
                if (item == null) return new(false, "历史项已不存在，请刷新");
                if (!await _clipboard.CopyToClipboardAsync(item)) return new(false, _clipboard.LastError ?? "复制失败");
                if (request.Operation == "clipboard.paste") { var target = _foreground.Target; _ = PasteAfterHideAsync(target); }
                return new(true, "已复制");
            case "clipboard.pin": if (item == null) return new(false, "历史项已不存在"); _clipboard.TogglePin(item); return new(true);
            case "clipboard.delete": if (item != null) _clipboard.DeleteItem(item); return new(true);
            case "clipboard.clear": _clipboard.ClearUnpinned(); return new(true);
            case "clipboard.preview":
                if (item?.ItemType != ClipboardItemType.Image) return new(false, "请选择图片");
                var bitmap = _clipboard.GetImage(item);
                if (bitmap != null) { var viewer = new ImageViewerWindow(bitmap); viewer.Show(); viewer.Activate(); }
                return new(true);
            case "tasks.list":
                var safeTasks = JsonSerializer.Deserialize<List<LaunchTask>>(JsonSerializer.Serialize(_tasks.Tasks))!;
                foreach (var task in safeTasks) task.EncryptedPassword = "";
                return new(true, _status, JsonSerializer.Serialize(safeTasks));
            case "tasks.save":
                var next = JsonSerializer.Deserialize<LaunchTask>(request.Data) ?? throw new ArgumentException("任务无效");
                LauncherSettings.Validate(next);
                var previous = _tasks.Tasks.FirstOrDefault(x => x.Id == next.Id);
                next.EncryptedPassword = previous?.EncryptedPassword ?? "";
                next.Fingerprint = previous != null && previous.Host == next.Host && previous.Port == next.Port ? previous.Fingerprint : "";
                var index = previous == null ? _tasks.Tasks.Count : _tasks.Tasks.IndexOf(previous);
                if (previous == null) _tasks.Tasks.Add(next); else _tasks.Tasks[index] = next;
                try { _tasks.Save(); } catch { if (previous == null) _tasks.Tasks.Remove(next); else _tasks.Tasks[index] = previous; throw; }
                return new(true, "任务已保存");
            case "tasks.delete":
                var deleted = _tasks.Tasks.FirstOrDefault(x => x.Id == request.Id);
                if (deleted != null) { _tasks.Tasks.Remove(deleted); try { _tasks.Save(); } catch { _tasks.Tasks.Add(deleted); throw; } }
                return new(true);
            case "tasks.run":
            case "tasks.test":
                var selected = _tasks.Tasks.FirstOrDefault(x => x.Id == request.Id) ?? throw new ArgumentException("任务不存在");
                LauncherSettings.Validate(selected);
                if (!File.Exists(LauncherSettings.SettingsPath)) _tasks.Save();
                if (request.Operation == "tasks.test") _ = TestAsync(selected); else _ = _runner.RunAsync(selected);
                return new(true, "已提交：" + selected.Name);
            case "tasks.stop": _runner.Stop(request.Id); return new(true, "停止请求已发送");
            case "tasks.password":
                var credentialTask = _tasks.Tasks.FirstOrDefault(x => x.Id == request.Id) ?? throw new ArgumentException("任务不存在");
                new CredentialWindow(credentialTask, _tasks.Save).Show(); return new(true);
            case "tasks.log":
                if (!Guid.TryParse(request.Id, out _)) throw new ArgumentException("任务 ID 无效");
                var path = TaskRunner.LogPath(request.Id);
                var text = File.Exists(path) ? File.ReadAllText(path) : "尚无执行日志";
                return new(true, _status, text.Length > 65536 ? text[^65536..] : text);
            case "options":
                var options = JsonSerializer.Deserialize<AgentOptions>(request.Data) ?? new();
                if (options.MaxHistory is < 10 or > 5000) throw new ArgumentException("历史数量应为 10–5000");
                AtomicFile.Write(Path.Combine(AgentProtocol.DataDirectory, "agent.json"), JsonSerializer.Serialize(options));
                _options = options; _clipboard.MaxHistoryCount = options.MaxHistory; return new(true);
            case "shutdown": _ = _app.Dispatcher.BeginInvoke(() => _app.Shutdown()); return new(true);
            default: return new(false, "不支持的操作");
        }
    }

    private async Task TestAsync(LaunchTask task)
    {
        try { await _runner.TestAsync(task); _status = task.Name + "：连接和 tcpdump 检查通过"; }
        catch (Exception ex) { _status = task.Name + "：" + ex.Message; }
    }

    private async Task PasteAfterHideAsync(IntPtr target)
    {
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(25);
            if (_foreground.PaletteActive) continue;
            // Host dismissal must restore the exact pre-palette window. Never guess a new target.
            if (target == IntPtr.Zero || PasteHelper.GetPreviousForegroundWindow() != target) break;
            _status = await PasteHelper.PasteToTargetAsync(_window, target, () => Task.FromResult(true)) ?? "已粘贴";
            return;
        }
        _status = "已复制，未确认原窗口焦点，请手动粘贴";
    }

    private static bool IsLink(ClipboardItem item) => item.ItemType == ClipboardItemType.Text && ClipboardLink.TryParse(item.Text, out _);

    private static string Thumbnail(ClipboardItem item, int[] budget)
    {
        if (item.ImageThumbnail == null) return "";
        // Keep a 50-item JSON response below the 4 MB IPC limit, including escaped text.
        var encoded = item.EncodedThumbnail ??= Convert.ToBase64String(ClipboardThumbnail.Encode(item.ImageThumbnail));
        if (encoded.Length > budget[0]) return "";
        budget[0] -= encoded.Length; return encoded;
    }

    public void Dispose()
    {
        _stop.Cancel(); _foreground.Dispose(); _monitor.Dispose(); _runner.Dispose(); _clipboard.Dispose(); _window.Close();
    }
}



