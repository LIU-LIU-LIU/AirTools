using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AirTools.Tools.Clipboard;
using AirTools.Tools.Clipboard.Services;

internal static class RackTests
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    public static void Run(Action<bool, string> check, string root, bool interactive)
    {
        var rack = new RecentTextRack { Capacity = 3 };
        foreach (var text in new[] { "one", "two", "three", "four" }) rack.Add(text);
        check(rack.Items.Select(x => x.Text).SequenceEqual(new[] { "four", "three", "two" }), "衣架容量与新旧顺序");
        var id = rack.Items[1].Id; rack.Add("three");
        check(rack.Items.Count == 3 && rack.Items[0].Id == id, "重复复制移到最前且复用条目");
        int changes = 0;
        ((System.Collections.Specialized.INotifyCollectionChanged)rack.Items).CollectionChanged += (_, _) => changes++;
        rack.Add("three"); check(changes == 0, "重复最新条目不触发衣架重绘");
        rack.Remove(id); check(rack.Items.Count == 2, "取下衣服"); rack.Add(" \r\n"); check(rack.Items.Count == 2, "忽略空文本");
        string longText = string.Concat(Enumerable.Repeat("完整长文本 🌿\r\n", 70)); rack.Add(longText);
        check(rack.Items[0].Preview.EndsWith("…") && rack.Items[0].Text == longText && rack.Items[0].IsLong, "省略预览保留完整长文本");
        rack.Capacity = 100; check(rack.Capacity == 5, "容量上限");
        rack.Add("这是一件短袖\n复制后点击粘贴"); rack.Add("第三件纯色衣服");
        // Pinning: a pinned text stays put and is never pushed out by new copies.
        var pinRack = new RecentTextRack { Capacity = 3 }; int pinEvents = 0; pinRack.PinsChanged += () => pinEvents++;
        foreach (var text in new[] { "a", "b", "c" }) pinRack.Add(text);                 // c b a
        check(pinRack.SetPinned(pinRack.Items[2].Id, true) && pinRack.Items[0].Text == "a" && pinRack.Items[0].Pinned, "固定后移到最靠近按钮的位置");
        pinRack.Add("d"); pinRack.Add("e");
        check(pinRack.Items.Select(x => x.Text).SequenceEqual(new[] { "a", "e", "d" }), "新复制不会挤掉固定的条目");
        check(pinRack.SetPinned(pinRack.Items[1].Id, true) && !pinRack.SetPinned(pinRack.Items[2].Id, true), "固定数量受限，至少留一个位置给新复制");
        pinRack.Add("a"); check(pinRack.Items[0].Text == "a" && pinRack.Items.Count == 3, "重复复制已固定的文本不改变位置");
        var unpinId = pinRack.Items[0].Id; pinRack.SetPinned(unpinId, false);
        check(!pinRack.Items.Single(x => x.Id == unpinId).Pinned && pinRack.Items[0].Pinned && pinRack.PinnedCount == 1, "取消固定后回到未固定区且其余固定项保持在前");
        check(pinEvents >= 3, "固定变化会通知保存");
        var restored = new RecentTextRack { Capacity = 5 }; restored.LoadPinned(new[] { "p1", "p2", "p1", "" });
        check(restored.Items.Select(x => x.Text).SequenceEqual(new[] { "p1", "p2" }) && restored.Items.All(x => x.Pinned), "重启后恢复固定项（去重去空）");
        restored.Add("x"); restored.Add("y"); restored.Add("z"); restored.Add("w");
        check(restored.Items.Count == 5 && restored.Items[0].Text == "p1" && restored.Items[1].Text == "p2", "固定项在容量已满时仍然保留");
        var pinWindow = new RackWindow(pinRack, _ => Task.FromResult<string?>(null), () => { }, () => { });
        pinWindow.ShowDocked(); Pump(Task.Delay(500)); pinRack.SetPinned(pinRack.Items[1].Id, true); Pump(Task.Delay(400)); pinWindow.PreviewHover(0); pinWindow.PreviewHover(null);
        check(pinWindow.SlotScreenPoint(0) is not null, "带固定条目的衣架可以正常显示和悬停"); pinWindow.Close();
        // Regression: pins restored AFTER the window was built (app startup) crashed ShowDocked with KeyNotFound.
        var startupRack = new RecentTextRack { Capacity = 5 };
        var startupWindow = new RackWindow(startupRack, _ => Task.FromResult<string?>(null), () => { }, () => { });
        startupRack.LoadPinned(new[] { "启动时恢复的固定项" });
        bool startupOk = true; try { startupWindow.ShowDocked(); Pump(Task.Delay(400)); } catch { startupOk = false; }
        check(startupOk && startupWindow.SlotScreenPoint(0) is not null, "启动时恢复固定项后显示衣架不崩溃"); startupWindow.Close();
        // Regression: first copy into an EMPTY rack used to animate an unpositioned card (NaN) and crash the app.
        var emptyRack = new RecentTextRack(); var emptyWindow = new RackWindow(emptyRack, _ => Task.FromResult<string?>(null), () => { }, () => { });
        emptyWindow.ShowDocked(); Pump(Task.Delay(300)); emptyRack.Add("第一次复制"); Pump(Task.Delay(700)); emptyRack.Add("第二次复制"); Pump(Task.Delay(700));
        emptyWindow.PreviewHover(0); Pump(Task.Delay(300)); emptyWindow.PreviewHover(null);
        check(emptyWindow.SlotScreenPoint(0) is not null, "空衣架首次复制不崩溃"); emptyWindow.Close();
        // Dragged to the top-left: the fan must open downward / rightward and stay on screen.
        var cornerRack = new RecentTextRack(); foreach (var text in new[] { "甲", "乙", "丙" }) cornerRack.Add(text);
        var cornerWindow = new RackWindow(cornerRack, _ => Task.FromResult<string?>(null), () => { }, () => { });
        cornerWindow.SetAnchor(80, 80); cornerWindow.ShowDocked(); Pump(Task.Delay(500));
        check(cornerWindow.FanDirection == (false, false), "拖到左上角后扇形朝右下展开");
        var cornerSlots = Enumerable.Range(0, 3).Select(i => cornerWindow.SlotScreenPoint(i)!.Value).ToArray();
        check(cornerSlots[0].Y < cornerSlots[1].Y && cornerSlots[1].Y < cornerSlots[2].Y && cornerSlots.All(p => p.X > 0 && p.Y > 0), "左上角时最新内容靠近按钮并向下排列且不出屏幕");
        cornerWindow.ResetPosition(); Pump(Task.Delay(300));
        check(cornerWindow.FanDirection == (true, true), "恢复默认位置回到右下角");
        cornerWindow.Close();
        var window = new RackWindow(rack, _ => Task.FromResult<string?>(null), () => { }, () => { }) { SlotHint = i => "Ctrl+Alt+" + (i + 1) };
        var before = GetForegroundWindow(); window.ShowDocked(); Pump(Task.Delay(500));
        check(GetForegroundWindow() == before, "显示衣架不激活窗口");
        var area = SystemParameters.WorkArea;
        check(Math.Abs(window.Left + window.Width - area.Right) < 1 && Math.Abs(window.Top + window.Height - area.Bottom) < 1, "衣架停靠在屏幕右下角");
        var slots = Enumerable.Range(0, rack.Items.Count).Select(i => window.SlotScreenPoint(i)!.Value).ToArray();
        check(slots.Zip(slots.Skip(1)).All(p => p.Second.Y < p.First.Y), "最新内容最靠近角落，向上依次排列");
        double rightmostEnd = Math.Max(slots[0].X, slots[^1].X);
        check(slots.Skip(1).SkipLast(1).Any(p => p.X < rightmostEnd - 10), "衣物沿弧线向左拱起");
        rack.Add("新复制的文本"); Pump(Task.Delay(600));
        check(window.SlotScreenPoint(0) is Point fresh && fresh.Y > slots[0].Y - 2, "新复制内容出现在最靠近角落的位置");
        check((NonActivatingWindow.GetStyles(window) & NonActivatingWindow.RequiredStyles) == NonActivatingWindow.RequiredStyles, "衣架应用非激活工具窗口样式");
        check(SendMessage(new WindowInteropHelper(window).Handle, 0x21, IntPtr.Zero, IntPtr.Zero).ToInt32() == 3, "点击衣架返回 MA_NOACTIVATE");
        foreach (bool dark in new[] { true, false })
        {
            window.ApplyTheme(dark);
            window.PreviewHover(null); SaveRack(window, Path.Combine(root, dark ? "rack-dark.png" : "rack-light.png"));
            window.PreviewHover(2); SaveRack(window, Path.Combine(root, dark ? "rack-hover-dark.png" : "rack-hover-light.png"));
        }
        window.PreviewHover(null);
        check(window.SlotScreenPoint(2) is Point idle && idle.Y > 0, "悬停预览恢复为收拢状态");
        window.Close();
        using (var limits = new ClipboardManagerService())
        {
            limits.Items.Clear();
            for (int i = 0; i < 6; i++) limits.Items.Add(new AirTools.Tools.Clipboard.Models.ClipboardItem { Text = "limit" + i, ContentHash = "limit" + i, CopyTime = DateTime.Now.AddDays(-i * 10), IsPinned = i == 5 });
            limits.Configure(3, 0, true, true, false, 128);
            check(limits.Items.Count == 3 && limits.Items.Any(x => x.IsPinned), "历史条数上限裁剪最旧记录并保留置顶");
            limits.Configure(100, 5, true, true, false, 128);
            check(limits.Items.Count == 2 && limits.Items.All(x => x.IsPinned || x.CopyTime > DateTime.Now.AddDays(-5)), "超过保留天数的记录被清理");
            limits.Items.Clear();
        }
        using (var history = new ClipboardManagerService())
        {
            int selfEvents = 0; history.ItemAdded += (_, _) => selfEvents++;
            var originalText = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
            check(PumpResult(history.CopyToClipboardAsync(new() { Text = longText })), "纯文本原生剪切板写入成功");
            for (int i = 0; i < 8; i++) { history.HandleClipboardChange(); Pump(Task.Delay(20)); }
            check(selfEvents == 0 && history.Items.Count == 0, "自身粘贴写入不回流到历史和衣架");
            check(System.Windows.Clipboard.GetText() == longText, "原生剪切板保留完整 Unicode 文本");
            if (originalText != null) history.CopyToClipboard(new() { Text = originalText });
            string import = Path.Combine(root, "plugin-history.json");
            File.WriteAllText(import, "[{\"ItemType\":0,\"Text\":\"imported original\",\"IsPinned\":true},{\"ItemType\":0,\"Text\":\"imported original\"}]");
            check(history.ImportHistory(import) == 1 && history.Items.Single().IsPinned, "旧插件文本导入去重并保留置顶");
            check(history.ImportHistory(import) == 0, "重复导入跳过已有文本");
            check(Directory.GetFiles(Path.Combine(root, "Clipboard", "Imports")).Length >= 1 && File.Exists(import), "导入保留来源并备份");
            File.WriteAllText(import, "invalid json"); bool failed = false;
            try { history.ImportHistory(import); } catch (System.Text.Json.JsonException) { failed = true; }
            check(failed && history.Items.Count == 1, "无效导入不破坏现有历史");
        }
        if (interactive) { TestPaste(root, true, check, longText); TestPaste(root, false, check, longText); }
    }

    private static void TestPaste(string root, bool native, Action<bool, string> check, string text)
    {
        string state = Path.Combine(root, native ? "native" : "wpf");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--paste-fixture"); start.ArgumentList.Add(state); start.ArgumentList.Add(native ? "native" : "wpf");
        using var fixture = Process.Start(start)!;
        RackWindow? window = null; GetCursorPos(out var cursor); var previous = GetForegroundWindow();
        try
        {
            for (int i = 0; i < 80 && !File.Exists(state + ".hwnd"); i++) Pump(Task.Delay(100));
            check(File.Exists(state + ".hwnd"), "粘贴目标测试窗口已就绪");
            var target = new IntPtr(long.Parse(File.ReadAllText(state + ".hwnd"))); ForceForeground(target); Pump(Task.Delay(200));
            if (GetForegroundWindow() != target && GetWindowRect(target, out var bounds))
            {
                SetCursorPos((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
                mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                Pump(Task.Delay(250));
            }
            if (GetForegroundWindow() != target)
            {
                check(false, "实际点击测试目标后仍无法获得焦点，交互粘贴未通过");
                return;
            }
            check(true, "测试目标获得焦点");
            var rack = new RecentTextRack(); rack.Add(text); bool clicked = false; string? error = null;
            using var clipboard = new ClipboardManagerService();
            window = new RackWindow(rack, async entry =>
            {
                clicked = true;
                error = await PasteHelper.PasteWithoutActivationAsync(GetForegroundWindow(), () => clipboard.CopyToClipboardAsync(new() { Text = entry.Text }));
                return error;
            }, () => { }, () => { });
            window.ShowDocked(); Pump(Task.Delay(600));
            var p = window.SlotScreenPoint(0)!.Value; SetCursorPos((int)p.X, (int)p.Y); Pump(Task.Delay(400));
            p = window.SlotScreenPoint(0)!.Value; SetCursorPos((int)p.X, (int)p.Y);
            mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            for (int i = 0; i < 60; i++)
            {
                Pump(Task.Delay(50));
                if (ReadState(state + ".text") == text) break;
            }
            check(clicked && error == null, (native ? "原生" : "兼容") + "衣服点击执行粘贴：" + error);
            check(GetForegroundWindow() == target, "真实鼠标点击后原目标保持焦点");
            check(ReadState(state + ".text") == text, (native ? "WM_PASTE" : "Shift+Insert") + "完整文本只粘贴一次");
        }
        finally { window?.Close(); if (!fixture.HasExited) { fixture.Kill(); fixture.WaitForExit(); } SetCursorPos(cursor.X, cursor.Y); PasteHelper.RestoreForeground(previous); }
    }
    private static void SaveRack(Window window, string path)
    {
        window.UpdateLayout();
        // Composite over a desktop-like gradient so the transparent window reads like it does on screen.
        int w = (int)window.ActualWidth, h = (int)window.ActualHeight;
        var layer = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); layer.Render(window);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x5B, 0x6B, 0x8C), Color.FromRgb(0xC9, 0xB8, 0xA8), 90), null, new Rect(0, 0, w, h));
            dc.DrawImage(layer, new Rect(0, 0, w, h));
        }
        var image = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static string? ReadState(string path) { try { return File.ReadAllText(path); } catch (IOException) { return null; } }
    private static void ForceForeground(IntPtr hwnd)
    {
        uint targetThread = GetWindowThreadProcessId(hwnd, out _), currentThread = GetCurrentThreadId();
        AttachThreadInput(currentThread, targetThread, true);
        ShowWindow(hwnd, 9); BringWindowToTop(hwnd); SetForegroundWindow(hwnd);
        AttachThreadInput(currentThread, targetThread, false);
    }

    public static int Fixture(string state, bool native)
    {
        if (native)
        {
            using var form = new System.Windows.Forms.Form { Text = "AirTools paste test", Width = 650, Height = 450, Left = 400, Top = 350, StartPosition = System.Windows.Forms.FormStartPosition.Manual, TopMost = true };
            var text = new System.Windows.Forms.TextBox { Multiline = true, Dock = System.Windows.Forms.DockStyle.Fill, MaxLength = 1000000 }; form.Controls.Add(text);
            using var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (_, _) => File.WriteAllText(state + ".text", text.Text);
            form.Shown += (_, _) => { text.Focus(); File.WriteAllText(state + ".hwnd", form.Handle.ToInt64().ToString()); timer.Start(); };
            System.Windows.Forms.Application.Run(form);
        }
        else
        {
            var app = new Application(); var text = new TextBox { AcceptsReturn = true };
            var window = new Window { Title = "AirTools compatibility paste test", Width = 650, Height = 450, Left = 400, Top = 350, Content = text, Topmost = true };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) }; timer.Tick += (_, _) => File.WriteAllText(state + ".text", text.Text);
            window.ContentRendered += (_, _) => { text.Focus(); File.WriteAllText(state + ".hwnd", new WindowInteropHelper(window).Handle.ToInt64().ToString()); timer.Start(); };
            app.Run(window);
        }
        return 0;
    }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false)); Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    private static T PumpResult<T>(Task<T> task) { Pump(task); return task.GetAwaiter().GetResult(); }
}
