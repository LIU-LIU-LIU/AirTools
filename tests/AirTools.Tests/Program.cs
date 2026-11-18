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
        if (args.Length >= 2 && args[0] == "--paste-fixture") return RackTests.Fixture(args[1], args.Length >= 3 && args[2] == "native");
        string root = Path.Combine(Path.GetTempPath(), "AirTools-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Environment.SetEnvironmentVariable("AIRTOOLS_DATA_DIRECTORY", root);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            RackTests.Run(Check, root, args.Contains("--interactive-paste"));
            ExtensionTests.Run(Check, root);
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
            using (var runner = new TaskRunner())
            {
                // "结束运行" must really kill what the task started, not just stop waiting for it.
                var program = new LaunchTask { Id = "stop-program", Kind = LaunchKind.Program, Executable = Path.Combine(Environment.SystemDirectory, "ping.exe"), Arguments = new[] { "-n", "60", "127.0.0.1" } };
                _ = runner.RunAsync(program); Pump(Task.Delay(900));
                Check(runner.IsRunning(program.Id), "程序任务运行中可被识别");
                Check(runner.Stop(program.Id), "结束运行返回成功");
                Pump(Task.Delay(900)); Check(!runner.IsRunning(program.Id) && !runner.Stop(program.Id), "程序任务被真正结束");
                var terminal = new LaunchTask { Id = "stop-terminal", Kind = LaunchKind.Terminal, Executable = Path.Combine(Environment.SystemDirectory, "ping.exe"), Arguments = new[] { "-n", "60", "127.0.0.1" } };
                var messages = new System.Collections.Generic.List<string>(); runner.StatusChanged += messages.Add;
                var run = runner.RunAsync(terminal); Pump(Task.Delay(2500));
                Check(runner.IsRunning(terminal.Id), "终端任务运行中可被识别：" + string.Join(" | ", messages));
                Check(runner.Stop(terminal.Id), "终端任务结束运行返回成功");
                Pump(Task.WhenAny(run, Task.Delay(8000)));
                Check(run.IsCompleted && !runner.IsRunning(terminal.Id), "终端任务被真正结束");
            }
            {
                var area = new Rect(0, 0, 1920, 1040);
                var corner = AirTools.Tools.SystemMonitor.Services.SnapService.Snap(14, 1040 - 40 - 6, 300, 40, area, true);
                Check(corner.SnappedX && corner.SnappedY && corner.Left == 8 && corner.Top == 1040 - 40 - 8 && corner.Anchor == "left-bottom", "靠近左下角自动吸附");
                var center = AirTools.Tools.SystemMonitor.Services.SnapService.Snap(1920 / 2 - 150 + 10, 5, 300, 40, area, true);
                Check(center.SnappedX && center.Left == 1920 / 2 - 150 && center.SnappedY && center.Top == 8, "靠近上边中点吸附居中");
                var far = AirTools.Tools.SystemMonitor.Services.SnapService.Snap(500, 400, 300, 40, area, true);
                Check(!far.SnappedX && !far.SnappedY && far.Left == 500 && far.Top == 400, "远离吸附点不吸附");
                var free = AirTools.Tools.SystemMonitor.Services.SnapService.Snap(14, 6, 300, 40, area, false);
                Check(!free.SnappedX && !free.SnappedY && free.Left == 14 && free.Top == 6, "按住 Shift/Ctrl 时不吸附");
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
                var warnings = hotkeys.Apply(new[] { ("", (Action)(() => { })), ("Ctrl+Alt+Shift+F9", (Action)(() => { })), ("Ctrl+Alt+Shift+F8", (Action)(() => { })) }, allowConflicts: true);
                Check(warnings.Count == 1 && warnings[0].Gesture == "Ctrl+Alt+Shift+F9", "清空不注册，其他冲突精确报告且允许应用");
                Check(rival.Apply(new[] { ("Ctrl+Alt+Shift+F10", (Action)(() => { })) }).Count == 0, "清空后的旧快捷键已释放");
                var probe = rival.Apply(new[] { ("Ctrl+Alt+Shift+F8", (Action)(() => { })) }, allowConflicts: true);
                Check(probe.Count == 1, "存在冲突时其余快捷键仍成功注册");
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
            TestImage(4000, 3000, 96); TestImage(600, 12000, 192); TestImage(2200, 1400, 144);
            config.Save();
            using (var host = new AppHost())
            {
                host.ShowSearch(); Pump(Task.Delay(200));
                var launcher = app.Windows.OfType<LauncherWindow>().Single();
                var results = (ListBox)launcher.FindName("Results"); var query = (TextBox)launcher.FindName("Query");
                Check(results.Items.Count == 5, "空词展示工具与任务");
                launcher.UpdateLayout();
                var rendered = new RenderTargetBitmap((int)launcher.ActualWidth, (int)launcher.ActualHeight,96,96,PixelFormats.Pbgra32); rendered.Render(launcher);
                var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered)); using(var stream=File.Create(Path.Combine(root,"launcher.png"))) encoder.Save(stream);
                Check(((TextBlock)launcher.FindName("SearchPlaceholder")).Text.Contains("c 搜索剪切板"), "输入栏内模式提示");
                query.Text = "Python"; query.Text = "Wireshark"; Pump(Task.Delay(180));
                Check(results.Items.Count == 1 && ((SearchResult)results.Items[0]).Title.Contains("Wireshark"), "过期搜索不覆盖新结果");
                typeof(LauncherWindow).GetMethod("SetMode", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(launcher, new object[] { SearchMode.Clipboard }); Pump(Task.Delay(150));
                Check(((Border)launcher.FindName("ModeBadge")).Visibility == Visibility.Visible, "剪切板模式标签");
                Check(results.Items.Count == 1, "剪切板搜索数据");
                host.ShowSettings(); Pump(Task.Delay(200)); Check(app.Windows.OfType<LauncherSettingsWindow>().Any(), "设置窗口创建");
                var settingsWindow = app.Windows.OfType<LauncherSettingsWindow>().Single();
                foreach (bool dark in new[] { false, true })
                {
                    Wpf.Ui.Appearance.ApplicationThemeManager.Apply(dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
                    Pump(Task.Delay(100)); Render(settingsWindow, Path.Combine(root, dark ? "settings-dark.png" : "settings-light.png"));
                }
                var navigation = Descendants(settingsWindow).OfType<Wpf.Ui.Controls.NavigationView>().Single();
                foreach (var item in navigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>())
                {
                    typeof(Wpf.Ui.Controls.NavigationViewItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(item, null);
                    Pump(Task.Delay(350));
                    Check(item.IsActive && Descendants(settingsWindow).Any(element => ReferenceEquals(element, item.Tag)), "导航点击实际切换：" + item.Content);
                    Render(settingsWindow, Path.Combine(root, "settings-" + item.Content + ".png"));
                }
                Check(navigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>().Count() == 8, "Fluent 设置导航各页面可渲染");
                var savePanel = (Border)typeof(LauncherSettingsWindow).GetField("_saveBar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settingsWindow)!;
                Check(savePanel.Visibility != Visibility.Visible, "未修改时不显示保存栏");
                var searchPage = navigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>().Single(item => (string)item.Content == "搜索启动器");
                typeof(Wpf.Ui.Controls.NavigationViewItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(searchPage, null); Pump(Task.Delay(300));
                var clearSearch = Descendants(settingsWindow).OfType<Wpf.Ui.Controls.Button>().Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "清除打开搜索");
                clearSearch.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump(Task.Delay(400));
                Check(savePanel.Visibility == Visibility.Visible, "修改快捷键后出现保存栏");
                ((Action)typeof(LauncherSettingsWindow).GetField("_saveGeneral", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settingsWindow)!)();
                var draft = (LauncherSettings)typeof(LauncherSettingsWindow).GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settingsWindow)!;
                host.ApplySettings(draft);
                Check(host.Settings.Hotkey == "" && LauncherSettings.Load().Hotkey == "", "Fluent 清除按钮实际保存空快捷键");
                host.RefreshTheme(); host.ShowClipboard(IntPtr.Zero); Pump(Task.Delay(150));
                var historyWindow = app.Windows.OfType<ClipboardWindow>().Single();
                Check(historyWindow is Wpf.Ui.Controls.FluentWindow && ((ListBox)historyWindow.FindName("LstClipboard")).Items.Count > 0, "Fluent 历史窗口保留列表功能");
                Render(historyWindow, Path.Combine(root, "history.png"));
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
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Render(Window window, string path)
    {
        window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
