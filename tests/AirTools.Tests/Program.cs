using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AirTools;
using AirTools.Services;
using AirTools.Tools.Clipboard;
using AirTools.Tools.Clipboard.Models;
using AirTools.Tools.Clipboard.Services;

internal static class Program
{
    private static int _count;
    [STAThread]
    public static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "AirTools-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Environment.SetEnvironmentVariable("AIRTOOLS_DATA_DIRECTORY", root);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            Check(SearchRanking.Score("Wireshark", "wsk") > 0, "模糊匹配");
            Check(SearchRanking.Score("Wireshark", "xyz") == 0, "无匹配");
            Check(SearchRanking.Score("ABC", "abc") > SearchRanking.Score("xabc", "abc"), "大小写与排序");
            Check(HotkeyParser.Parse("Alt+Space") == (0x4001u, 0x20u), "Alt+Space 映射");
            Check((HotkeyParser.Parse("Win+V").Modifiers & 8) != 0, "Win 修饰键");
            Check(HotkeyParser.Parse("Ctrl+OemComma").Key == 0xBC, "标点虚拟键");
            Check(TaskRunner.QuotePs("a'b") == "'a''b'", "PowerShell 参数转义");
            Check(TaskRunner.BuildInvocation(new LaunchTask { Executable = "some program.exe", Arguments = new[] { "a b", "$(bad)" } }) == "& 'some program.exe' 'a b' '$(bad)'", "参数独立且不执行插值");
            string secret = TaskRunner.Protect("local-test"); Check(TaskRunner.Unprotect(secret) == "local-test" && !secret.Contains("local-test"), "DPAPI 往返");
            var config = LauncherSettings.Load(); Check(config.Tasks.Count == 2, "预设任务");
            config.Hotkey = "Ctrl+Alt+Shift+F10"; config.ClipboardHotkey = "Ctrl+Alt+Shift+F11"; config.Directories.Clear(); config.Save();
            Check(LauncherSettings.Load().Tasks[0].Arguments.Single() == @".\adb_vpn_monitor.py", "配置原子保存");
            Directory.CreateDirectory(Path.Combine(root,"Logs"));
            string pythonFixture=Path.Combine(root,"fixture.py");
            File.WriteAllText(pythonFixture,"import sys\nprint('自动选中唯一设备: 158edfc9')\nprint('普通诊断提示', file=sys.stderr)\nsys.exit(int(sys.argv[1]))\n", new System.Text.UTF8Encoding(false));
            foreach(int expectedExit in new[]{0,7})
            {
                var terminalTask=new LaunchTask { Id="terminal-"+expectedExit,Kind=LaunchKind.Terminal,Executable="python.exe",Arguments=new[]{pythonFixture,expectedExit.ToString()} };
                var start=new ProcessStartInfo("powershell.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
                start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(TaskRunner.BuildTerminalScript(terminalTask))));
                using var process=Process.Start(start)!;
                var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
                Check(process.WaitForExit(15000),"终端测试按时结束");
                Check(process.ExitCode==expectedExit,"终端按真实退出码判断："+expectedExit);
                string log=File.ReadAllText(TaskRunner.LogPath(terminalTask.Id));
                Check(log.Contains("自动选中唯一设备") && log.Contains("普通诊断提示"),"Python stdout/stderr 中文 UTF-8");
            }
            using (var hotkeys = new GlobalHotkeys())
            using (var rival = new GlobalHotkeys())
            {
                hotkeys.Apply(new[] { ("Ctrl+Alt+Shift+F10", (Action)(() => { })) });
                rival.Apply(new[] { ("Ctrl+Alt+Shift+F9", (Action)(() => { })) });
                bool failed = false; try { hotkeys.Apply(new[] { ("Ctrl+Alt+Shift+F9", (Action)(() => { })) }); } catch { failed = true; }
                Check(failed, "快捷键冲突被拒绝");
                bool retained = false; try { rival.Apply(new[] { ("Ctrl+Alt+Shift+F10", (Action)(() => { })) }); } catch { retained = true; }
                Check(retained, "注册失败保留原快捷键");
                bool duplicate = false; try { hotkeys.Apply(new[] { ("Ctrl+V", (Action)(() => { })), ("Control+V", (Action)(() => { })) }); } catch { duplicate = true; }
                Check(duplicate, "重复快捷键拒绝");
            }
            string historyDir = Path.Combine(root, "Clipboard"); Directory.CreateDirectory(historyDir);
            File.WriteAllText(Path.Combine(historyDir, "history.json"), "[{\"Id\":\"legacy\",\"ItemType\":0,\"Text\":\"历史文本\",\"IsPinned\":true,\"ContentHash\":\"h\"}]");
            using (var clipboard = new ClipboardManagerService())
            {
                Check(clipboard.Items.Single().Text == "历史文本" && clipboard.Items.Single().IsPinned, "历史迁移与置顶");
                var window = new Window(); bool copied = false;
                string? error = Pump(PasteHelper.PasteToTargetAsync(window, IntPtr.Zero, () => { copied = true; return Task.FromResult(false); }));
                Check(copied && error?.Contains("复制失败") == true, "复制失败终止粘贴"); window.Close();
            }
            Check(File.ReadAllText(Path.Combine(historyDir, "history.json")).Contains("legacy"), "退出刷新历史");
            string filesDir = Path.Combine(root, "files"); Directory.CreateDirectory(filesDir); File.WriteAllText(Path.Combine(filesDir, "needle.txt"), ""); Directory.CreateDirectory(Path.Combine(filesDir, "node_modules")); File.WriteAllText(Path.Combine(filesDir, "node_modules", "hidden.txt"), "");
            config.Directories.Add(filesDir);
            using (var files = new FileSearchService())
            {
                files.Rebuild(config.Directories); Pump(Task.Delay(250));
                Check(files.Search("needle", config, default).Length == 1, "后台文件索引");
                Check(files.Search("hidden", config, default).Length == 0, "排除依赖目录");
                File.WriteAllText(Path.Combine(filesDir, "created.txt"), "");
                for(int i=0;i<30 && files.Search("created.txt",config,default).Length==0;i++) Pump(Task.Delay(100));
                Check(files.Search("created.txt", config, default).Length == 1, "增量文件更新：" + files.Status);
            }
            TestImage(4000, 3000, 96); TestImage(600, 12000, 192); TestImage(2200, 1400, 144);
            config.Directories.Clear(); config.Save();
            using (var host = new AppHost())
            {
                host.ShowSearch(); Pump(Task.Delay(200));
                var launcher = app.Windows.OfType<LauncherWindow>().Single();
                var results = (ListBox)launcher.FindName("Results"); var query = (TextBox)launcher.FindName("Query");
                Check(results.Items.Count == 6, "空词展示工具与任务");
                launcher.UpdateLayout();
                var rendered = new RenderTargetBitmap((int)launcher.ActualWidth, (int)launcher.ActualHeight,96,96,PixelFormats.Pbgra32); rendered.Render(launcher);
                var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered)); using(var stream=File.Create(Path.Combine(root,"launcher.png"))) encoder.Save(stream);
                Check(((TextBlock)launcher.FindName("SearchPlaceholder")).Text.Contains("c 剪切板"), "输入栏内模式提示");
                query.Text = "Python"; query.Text = "Wireshark"; Pump(Task.Delay(180));
                Check(results.Items.Count == 1 && ((SearchResult)results.Items[0]).Title.Contains("Wireshark"), "过期搜索不覆盖新结果");
                typeof(LauncherWindow).GetMethod("SetMode", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(launcher, new object[] { SearchMode.Clipboard }); Pump(Task.Delay(150));
                Check(((Border)launcher.FindName("ModeBadge")).Visibility == Visibility.Visible, "剪切板模式标签");
                Check(results.Items.Count == 1, "剪切板搜索数据");
                host.ShowSettings(); Pump(Task.Delay(100)); Check(app.Windows.OfType<LauncherSettingsWindow>().Any(), "设置窗口创建");
            }
            Console.WriteLine($"PASS {_count} checks; artifacts: {root}"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void TestImage(int width, int height, double dpi)
    {
        var image = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4); image.Freeze();
        var window = new ImageViewerWindow(image); window.Show(); window.UpdateLayout(); Pump(Task.Delay(30));
        var preview = (Image)window.FindName("ImgPreview"); var canvas = (Border)window.FindName("CanvasHost");
        var matrix = ((MatrixTransform)preview.RenderTransform).Matrix;
        Check(preview.ActualWidth == width && preview.ActualHeight == height, $"原图布局完整 {width}x{height}@{dpi}");
        Check(matrix.OffsetX >= -1 && matrix.OffsetY >= -1 && matrix.OffsetX + width * matrix.M11 <= canvas.ActualWidth + 1 && matrix.OffsetY + height * matrix.M22 <= canvas.ActualHeight + 1, "适应后四边可见");
        window.Close();
    }
    private static T Pump<T>(Task<T> task) { Pump((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        // Capture the UI dispatcher before continuing on a worker.
        var dispatcher = Dispatcher.CurrentDispatcher;
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); _count++; Console.WriteLine("PASS " + name); }
}
