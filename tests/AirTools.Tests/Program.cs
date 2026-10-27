using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AirTools.Core;
using AirTools.Services;
using AirTools.Tools.Clipboard;
using AirTools.Tools.Clipboard.Models;
using AirTools.Tools.Clipboard.Services;

internal static class Program
{
    private static int _checks;
    private static void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); _checks++; }
    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "AirTools-extension-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AIRTOOLS_DATA_DIRECTORY", root); Directory.CreateDirectory(root);
        try
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            CoreTests(root);
            ImageTests();
            TerminalTests(root);
            if (args.Length > 0) AgentTests(args[0], root).GetAwaiter().GetResult();
            Console.WriteLine($"{_checks} checks passed. Isolated data: {root}"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void CoreTests(string root)
    {
        Check(SearchRanking.Score("Wireshark", "wsk") > 0, "fuzzy matching");
        Check(SearchRanking.Score("Wireshark", "xyz") == 0, "unmatched query");
        Check(SearchRanking.Score("ABC", "abc") > SearchRanking.Score("xabc", "abc"), "case insensitive ranking");
        using var query = new LatestQuery(); var first = query.Begin(); var second = query.Begin();
        Check(first.IsCancellationRequested && !second.IsCancellationRequested, "obsolete queries cancelled");
        var result = "new";
        Check(!query.TryPublish(first, () => result = "stale") && result == "new", "obsolete response cannot replace latest results");
        Check(query.TryPublish(second, () => result = "current") && result == "current", "current response can publish");
        query.Dispose(); Check(query.Begin().IsCancellationRequested && !query.TryPublish(second, () => result = "disposed"), "disposed page cannot restart searches");
        var task = new LaunchTask { Executable = "some program.exe", Arguments = ["a b", "$(bad)", "a'b"] };
        Check(TaskRunner.BuildInvocation(task) == "& 'some program.exe' 'a b' '$(bad)' 'a''b'", "literal argument boundaries");
        Check(TaskRunner.QuoteSh("a'b") == "'a'\"'\"'b'", "remote interface quoting");
        Check(TaskRunner.Unprotect(TaskRunner.Protect("仅本地测试")) == "仅本地测试", "DPAPI roundtrip");
        LauncherSettings.Validate(task); task.Id = "../escape";
        try { LauncherSettings.Validate(task); throw new Exception("unsafe ID accepted"); } catch (ArgumentException) { Check(true, "task ID traversal rejected"); }
        var source = Path.Combine(root, "legacy"); var target = Path.Combine(root, "import"); Directory.CreateDirectory(Path.Combine(source, "Clipboard"));
        File.WriteAllText(Path.Combine(source, "launcher.json"), "{\"Tasks\":[{\"Name\":\"保留任务\",\"Arguments\":[\"a b\"]}]}" );
        File.WriteAllText(Path.Combine(source, "Clipboard", "history.json"), "[{\"Id\":\"old\",\"Text\":\"保留历史\",\"IsPinned\":true}]");
        LegacyImport.Import(source, target);
        Check(File.Exists(Path.Combine(target, "launcher.json.import-backup")), "migration keeps backup");
        Check(File.ReadAllText(Path.Combine(target, "Clipboard", "history.json")).Contains("保留历史"), "history pin migration");
        File.WriteAllText(Path.Combine(target, "launcher.json"), "{}"); LegacyImport.Import(source, target);
        Check(File.ReadAllText(Path.Combine(target, "launcher.json")) == "{}", "migration never overwrites new configuration");
        using var invalid = new MemoryStream(BitConverter.GetBytes(AgentProtocol.MaximumMessageBytes + 1));
        try { AgentProtocol.ReadAsync<AgentRequest>(invalid, default).GetAwaiter().GetResult(); throw new Exception("oversize accepted"); }
        catch (IOException) { Check(true, "oversize IPC rejected before allocation"); }
        using var stream = new MemoryStream(); AgentProtocol.WriteAsync(stream, new AgentRequest("status"), default).GetAwaiter().GetResult(); stream.Position = 0;
        Check(AgentProtocol.ReadAsync<AgentRequest>(stream, default).GetAwaiter().GetResult().Operation == "status", "framed IPC roundtrip");
        var defaults = LauncherSettings.Load(); Check(defaults.Tasks.Count == 2, "two task presets"); defaults.Save();
        Check(LauncherSettings.Load().Tasks[0].WorkingDirectory.EndsWith("QuasarVPN", StringComparison.Ordinal), "working directory preserved");
    }
    private static void ImageTests()
    {
        using var service = new ClipboardManagerService();
        Check(!service.CopyToClipboardAsync(new ClipboardItem { ItemType = ClipboardItemType.Files, FilePaths = null }).GetAwaiter().GetResult(), "invalid file history cannot report successful copy");
        foreach (var size in new[] { (4000, 2000, 96d), (300, 10000, 144d), (100, 100, 192d) })
        {
            var bitmap = BitmapSource.Create(size.Item1, size.Item2, size.Item3, size.Item3, PixelFormats.Bgra32, null, new byte[size.Item1 * size.Item2 * 4], size.Item1 * 4);
            using var stream = new MemoryStream(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
            var item = new ClipboardItem { ItemType = ClipboardItemType.Image, ImageData = stream.ToArray() };
            var decoded = service.GetImage(item)!;
            Check(decoded.PixelWidth == size.Item1 && decoded.PixelHeight == size.Item2, "full image decode " + size);
            var viewer = new ImageViewerWindow(decoded) { Width = 1000, Height = 720, ShowActivated = false };
            viewer.Show(); viewer.UpdateLayout();
            var image = (System.Windows.Controls.Image)viewer.FindName("ImgPreview");
            var viewport = (FrameworkElement)viewer.FindName("CanvasHost");
            var bounds = image.TransformToAncestor(viewport).TransformBounds(new Rect(0, 0, image.ActualWidth, image.ActualHeight));
            Check(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= viewport.ActualWidth + 1 && bounds.Bottom <= viewport.ActualHeight + 1, "full image fits viewport " + size);
            viewer.Close();
        }
    }
    private static void TerminalTests(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Logs"));
        var fixture = Path.Combine(root, "fixture.py");
        File.WriteAllText(fixture, "import sys\nprint('自动选中唯一设备: 158edfc9')\nprint('诊断信息',file=sys.stderr)\nsys.exit(int(sys.argv[1]))\n", new UTF8Encoding(false));
        foreach (var exit in new[] { 0, 7 })
        {
            var task = new LaunchTask { Executable = "python.exe", Kind = LaunchKind.Terminal, Arguments = [fixture, exit.ToString()] };
            var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
            info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-EncodedCommand"); info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(TaskRunner.BuildTerminalScript(task))));
            using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            Check(process.WaitForExit(15000), "terminal exits promptly " + exit);
            Check(process.ExitCode == exit, "native exit code " + exit);
            var log = File.ReadAllText(TaskRunner.LogPath(task.Id));
            Check(log.Contains("自动选中唯一设备") && log.Contains("诊断信息"), "UTF-8 stdout/stderr " + exit);
        }
    }
    private static async Task AgentTests(string executable, string root)
    {
        AtomicFile.Write(Path.Combine(root, ".imported"), "test");
        Directory.CreateDirectory(Path.Combine(root, "Clipboard"));
        AtomicFile.Write(Path.Combine(root, "Clipboard", "history.json"), "[{\"Id\":\"seed\",\"ItemType\":0,\"Text\":\"测试历史\",\"IsPinned\":true}]");
        var cold = Stopwatch.StartNew();
        using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false })!;
        try
        {
            var status = await AgentProtocol.SendAsync(new("status")); Check(status.Success, "Agent starts and accepts same-user IPC");
            Console.WriteLine($"METRIC Agent cold start + first IPC: {cold.Elapsed.TotalMilliseconds:F1} ms");
            var history = await AgentProtocol.SendAsync(new("clipboard.list")); Check(history.Data.Contains("seed"), "Agent loads migrated history");
            var bad = await AgentProtocol.SendAsync(new("clipboard.paste", "missing")); Check(!bad.Success, "missing item cannot copy or send paste");
            var task = new LaunchTask { Name = "integration task", Executable = "cmd.exe", Arguments = ["/c", "exit", "0"], WorkingDirectory = root };
            Check((await AgentProtocol.SendAsync(new("tasks.save", Data: JsonSerializer.Serialize(task)))).Success, "task CRUD save");
            Check((await AgentProtocol.SendAsync(new("tasks.list"))).Data.Contains("integration task"), "task CRUD list");
            Check((await AgentProtocol.SendAsync(new("tasks.delete", task.Id))).Success, "task CRUD delete");
            Check(!(await AgentProtocol.SendAsync(new("tasks.list"))).Data.Contains("integration task"), "task deleted from results");
            Check((await AgentProtocol.SendAsync(new("options", Data: JsonSerializer.Serialize(new AgentOptions(false, 100))))).Success, "pause recording through settings");
            Check((await AgentProtocol.SendAsync(new("clipboard.clear"))).Success, "clear unpinned");
            Check((await AgentProtocol.SendAsync(new("clipboard.list"))).Data.Contains("seed"), "clear preserves pins");
            Check((await AgentProtocol.SendAsync(new("clipboard.pin", "seed"))).Success, "unpin");
            await AgentProtocol.SendAsync(new("clipboard.clear"));
            Check(JsonSerializer.Deserialize<ClipboardEntry[]>((await AgentProtocol.SendAsync(new("clipboard.list"))).Data)!.Length == 0, "clear removes unpinned");
            var malformed = new LaunchTask { Id = "../bad", Executable = "cmd.exe" };
            Check(!(await AgentProtocol.SendAsync(new("tasks.save", Data: JsonSerializer.Serialize(malformed)))).Success, "Agent validates untrusted tasks");
            Check((await AgentProtocol.SendAsync(new("status"))).Success, "Agent remains responsive after invalid request");
            var latency = new List<double>();
            for (var n = 0; n < 100; n++)
            {
                var elapsed = Stopwatch.StartNew(); await AgentProtocol.SendAsync(new("clipboard.list", Query: "测试")); latency.Add(elapsed.Elapsed.TotalMilliseconds);
            }
            latency.Sort();
            await Task.Delay(1000); process.Refresh(); var cpuStart = process.TotalProcessorTime; var idle = Stopwatch.StartNew();
            await Task.Delay(10000); process.Refresh();
            Console.WriteLine($"METRIC Agent only, paused recording, empty history: working set {process.WorkingSet64 / 1048576d:F1} MB; private bytes {process.PrivateMemorySize64 / 1048576d:F1} MB; idle CPU {(process.TotalProcessorTime - cpuStart).TotalMilliseconds / idle.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100:F3}% over 10 s; IPC query P95 {latency[94]:F2} ms (100 queries, excludes host + 80 ms debounce)");
        }
        finally { await AgentProtocol.SendAsync(new("shutdown")); Check(process.WaitForExit(5000), "Agent shuts down cleanly"); }
    }
}
