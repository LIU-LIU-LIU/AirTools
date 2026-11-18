using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ui = Wpf.Ui.Controls;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using AirTools.Services;
using AirTools.Tools.SystemMonitor.Models;

namespace AirTools;

/// <summary>
/// PowerToys-style settings: a home dashboard plus one page per tool. Each tool page opens with a hero
/// banner and feature callouts, then setting cards. Edits go to a draft; a save bar slides in when dirty.
/// </summary>
public sealed class LauncherSettingsWindow : Ui.FluentWindow
{
    private readonly AppHost _host;
    private readonly LauncherSettings _draft;
    private readonly Ui.NavigationView _navigation;
    private readonly StackPanel _form = new() { Margin = new Thickness(0, 0, 4, 24) };
    private readonly ListBox _tasks = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly Border _saveBar, _toast;
    private readonly TextBlock _saveText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly TextBlock _toastText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly Ui.SymbolIcon _toastIcon = new() { FontSize = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dictionary<string, Ui.NavigationViewItem> _pages = new();
    private readonly Action _saveGeneral;
    private readonly Action _saveTheme;
    private Action? _commitFields;
    private bool _loading = true, _dirty, _silent;
    /// <summary>App shutdown: close without the unsaved-changes prompt.</summary>
    public void CloseSilently() { _silent = true; Close(); }
    private int _changes;
    private string? _pendingPage;

    public LauncherSettingsWindow(AppHost host)
    {
        _host = host; _draft = host.Settings.Clone();
        Title = "AirTools 设置"; Width = 960; Height = 680; MinWidth = 760; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowBackdropType = Ui.WindowBackdropType.Mica;
        WindowCornerPreference = Ui.WindowCornerPreference.Round;
        ExtendsContentIntoTitleBar = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI");

        var root = new Grid(); Content = root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var titleBar = new Ui.TitleBar { Title = "AirTools 设置", ShowMaximize = true, Height = 44 };
        try { titleBar.Icon = new Ui.ImageIcon { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/AirTools;component/Assets/AirTools.ico")), Width = 16, Height = 16 }; } catch { }
        root.Children.Add(titleBar);

        _navigation = new Ui.NavigationView
        {
            PaneDisplayMode = Ui.NavigationViewPaneDisplayMode.Left, OpenPaneLength = 204, IsPaneOpen = true, IsPaneToggleVisible = false,
            IsBackButtonVisible = Ui.NavigationViewBackButtonVisible.Collapsed, HeaderVisibility = Visibility.Collapsed, FrameMargin = new Thickness(0),
        };
        Grid.SetRow(_navigation, 1); root.Children.Add(_navigation);

        // ---------- pages ----------
        var home = new StackPanel();
        var rack = ToolPage("晾衣架", "把最近复制的文字收在屏幕右下角；平时只露出一枚小按钮，鼠标靠近即沿弧线展开，点一下就粘贴。",
            "#6A5AE0", "#E2609E", RackMock(), ("\uE7C9", "不抢焦点", "悬浮窗不会激活，正在输入的窗口保持焦点"), ("\uE8B9", "弧形展开", "像 Dock 堆栈一样从角落拉出一道彩虹"), ("\uE77F", "一键粘贴", "点击即粘贴完整原文，超长内容也不截断"));
        var history = ToolPage("剪切板历史", "浏览、搜索与置顶复制过的文本、图片和文件，随时找回刚才的内容。",
            "#1F6FE5", "#29B6D8", HistoryMock(), ("\uE81C", "完整历史", "文本、图片与文件统一记录"), ("\uE718", "置顶常用", "常用片段固定在最上方"), ("\uE721", "即时搜索", "输入关键字立即过滤"));
        var search = ToolPage("搜索启动器", "一个快捷键唤出搜索栏：运行快速命令，或输入模式键切换到剪切板搜索。",
            "#3949D6", "#8A5CF6", SearchMock(), ("\uE945", "快速", "常驻后台，按下即出现"), ("\uE8B7", "快速命令", "按名称或别名运行任务"), ("\uE8FD", "剪切板搜索", "输入模式键搜索历史并粘贴"));
        var monitor = ToolPage("系统监控", "置顶的小型资源条，显示 CPU、内存、磁盘与网络速率，可拖动摆放。",
            "#14956A", "#3CC6A4", MonitorMock(), ("\uE9D9", "实时", "按设定间隔刷新"), ("\uE7F4", "可拖动", "放在任意屏幕边缘"), ("\uE790", "主题跟随", "浅色与深色自动适配"));
        var palette = ToolPage("命令面板扩展", "在 PowerToys 命令面板里直接搜索快速命令和剪切板历史。扩展只是 AirTools 的入口，设置仍在这里。",
            "#2B6CB0", "#63B3ED", PaletteMock(), ("", "快速命令", "按名称或别名运行任务"), ("", "剪切板", "搜索历史并粘贴、置顶"), ("", "一键安装", "无需证书和管理员权限"));
        var general = Page("常规", "外观、启动与数据。");
        var tasksPage = new Grid();

        AddHeader("概览");
        AddPage("主页", Ui.SymbolRegular.Home24, home);
        AddHeader("工具");
        AddPage("晾衣架", Ui.SymbolRegular.ClipboardPaste24, rack);
        AddPage("剪切板历史", Ui.SymbolRegular.History24, history);
        AddPage("搜索启动器", Ui.SymbolRegular.Search24, search);
        AddPage("快速启动", Ui.SymbolRegular.Rocket24, tasksPage);
        AddPage("系统监控", Ui.SymbolRegular.Gauge24, monitor);
        AddPage("命令面板", Ui.SymbolRegular.PuzzlePiece24, palette);
        AddHeader("设置");
        AddPage("常规", Ui.SymbolRegular.Settings24, general);

        // ---------- rack ----------
        var rackEnabled = Toggle(_draft.RackEnabled);
        Card(rack, Ui.SymbolRegular.ClipboardPaste24, "晾衣架", "在屏幕右下角显示，不使用时自动收拢。", rackEnabled, prominent: true);
        var rackKey = Recorder(rack, Ui.SymbolRegular.Keyboard24, "显示 / 隐藏晾衣架", "全局快捷键，随时收起或唤出。", _draft.RackHotkey);
        var capacity = Combo(new[] { "3 件", "4 件", "5 件" }, Math.Clamp(_draft.RackCapacity, 3, 5) - 3);
        Section(rack, "行为");
        Card(rack, Ui.SymbolRegular.Apps24, "保留数量", "最新复制的内容最靠近角落按钮，预览省略不影响完整粘贴。", capacity);
        var fallback = Toggle(_draft.PasteCompatibilityFallback);
        Card(rack, Ui.SymbolRegular.PlugConnected24, "兼容粘贴", "标准输入框使用 Windows 粘贴消息；浏览器、终端等改用 Shift+Insert。", fallback);
        Section(rack, "位置");
        Card(rack, Ui.SymbolRegular.ArrowReset24, "恢复到右下角", "拖动角落按钮可以换位置，扇形会自动朝屏幕内侧展开；右键按钮可隐藏、收起或打开设置。", ActionButton("恢复", Ui.SymbolRegular.ArrowReset24, _host.ResetRackPosition));
        Section(rack, "快速粘贴");
        var slotsExpander = Expander(rack, Ui.SymbolRegular.Flash24, "按位置粘贴", "不用移动鼠标，直接用快捷键粘贴第 N 件。第 1 件为最新复制。");
        var slots = new Shortcut[5];
        for (int i = 0; i < 5; i++) slots[i] = Recorder(slotsExpander, null, $"粘贴第 {i + 1} 件", "", i < _draft.RackSlotHotkeys.Length ? _draft.RackSlotHotkeys[i] : "", nested: true);

        // ---------- history ----------
        var clip = Recorder(history, Ui.SymbolRegular.Keyboard24, "打开剪切板历史", "在任意位置唤出历史窗口，双击条目粘贴回原窗口。", _draft.ClipboardHotkey, prominent: true);
        Section(history, "记录");
        var pause = Toggle(_draft.ClipboardPaused);
        Card(history, Ui.SymbolRegular.Timer24, "暂停记录", "开启后不再记录新的复制内容，已有历史不受影响；复制密码时很有用。", pause);
        var recImages = Toggle(_draft.ClipboardRecordImages);
        Card(history, Ui.SymbolRegular.Desktop24, "记录图片", "关闭后复制的图片不会进入历史（图片仅保存在内存中，不写入磁盘）。", recImages);
        var recFiles = Toggle(_draft.ClipboardRecordFiles);
        Card(history, Ui.SymbolRegular.Folder24, "记录文件", "关闭后复制的文件列表不会进入历史。", recFiles);
        Section(history, "容量与保留");
        var (maxItems, maxItemsValues) = IntCombo(new[] { 50, 100, 200, 500, 1000, 2000, 5000 }, _draft.ClipboardMaxItems, n => $"{n} 条");
        Card(history, Ui.SymbolRegular.Apps24, "最多保留条数", "超出后自动删除最旧的未置顶记录；置顶项永远不会被删除。", maxItems);
        var (retention, retentionValues) = IntCombo(new[] { 0, 1, 7, 30, 90, 365 }, _draft.ClipboardRetentionDays, n => n == 0 ? "不限制" : $"{n} 天");
        Card(history, Ui.SymbolRegular.Timer24, "保留时长", "超过这个时间的未置顶记录会被自动清理。", retention);
        var (imageBudget, imageBudgetValues) = IntCombo(new[] { 32, 64, 128, 256, 512, 1024 }, _draft.ClipboardImageBudgetMb, n => $"{n} MB");
        Card(history, Ui.SymbolRegular.Server24, "图片内存上限", "所有图片合计占用的内存上限，超出后删除最旧的图片。", imageBudget);
        Section(history, "数据");
        Card(history, Ui.SymbolRegular.Open24, "历史窗口", "查看全部记录、置顶或清理。", ActionButton("打开", Ui.SymbolRegular.Open24, () => _host.ShowClipboard(IntPtr.Zero)));
        Card(history, Ui.SymbolRegular.Delete24, "清空历史", "删除全部未置顶的记录，置顶项保留。此操作不能撤销。", ActionButton("清空…", Ui.SymbolRegular.Delete24, () =>
        {
            if (MessageBox.Show(this, "确定删除所有未置顶的剪切板历史吗？\n\n置顶条目会保留。", "清空历史", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _host.Clipboard.ClearUnpinned(); Toast("已清空未置顶的历史", Ui.InfoBarSeverity.Success);
        }));
        Card(history, Ui.SymbolRegular.ArrowImport24, "导入旧版历史", "导入原 Command Palette 插件的文本历史，源文件会先备份，重复项自动跳过。", ActionButton("选择文件…", null, () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "剪切板历史 (history.json)|history.json|JSON 文件 (*.json)|*.json", InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirTools.CommandPalette", "Clipboard") };
            if (dialog.ShowDialog(this) == true) Toast($"已导入 {_host.Clipboard.ImportHistory(dialog.FileName)} 条文本", Ui.InfoBarSeverity.Success);
        }));

        // ---------- search ----------
        var launch = Recorder(search, Ui.SymbolRegular.Keyboard24, "打开搜索", "按下快捷键唤出搜索栏，Esc 收起。", _draft.Hotkey, prominent: true);
        Section(search, "搜索模式");
        var c = ModeKey(_draft.ClipboardModeKey);
        Card(search, Ui.SymbolRegular.ClipboardTextLtr24, "剪切板模式键", "在空搜索框输入此字母，切换到剪切板搜索；其余时候搜索快速命令。", c);

        // ---------- monitor ----------
        var ms = MonitorSettings.Load();
        Card(monitor, Ui.SymbolRegular.Gauge24, "资源监控条", "置顶小条，按住拖动可摆放到任意位置；右键有更多选项。", ActionButton("显示 / 隐藏", Ui.SymbolRegular.Open24, _host.ToggleMonitor, accent: true), prominent: true);
        Section(monitor, "显示内容");
        var showCpu = Toggle(ms.ShowCpu); Card(monitor, Ui.SymbolRegular.Flash24, "处理器 (CPU)", "", showCpu);
        var showMem = Toggle(ms.ShowMemory); Card(monitor, Ui.SymbolRegular.Apps24, "内存 (RAM)", "", showMem);
        var showDisk = Toggle(ms.ShowDisk); Card(monitor, Ui.SymbolRegular.Server24, "磁盘读写", "", showDisk);
        var showNet = Toggle(ms.ShowNetwork); Card(monitor, Ui.SymbolRegular.Globe24, "网络上下行", "", showNet);
        Section(monitor, "刷新与设备");
        var (interval, intervalValues) = IntCombo(new[] { 500, 1000, 2000, 3000, 5000 }, ms.UpdateIntervalMs, n => n < 1000 ? $"{n} 毫秒" : $"{n / 1000.0:0.#} 秒");
        Card(monitor, Ui.SymbolRegular.Timer24, "刷新间隔", "间隔越短越实时，占用也略高。", interval);
        var drives = DriveInfo.GetDrives().Where(drv => drv.DriveType == DriveType.Fixed && drv.IsReady).Select(drv => drv.Name.TrimEnd('\\')).OrderBy(name => name).ToList();
        if (drives.Count == 0) drives.Add("C:");
        var driveBox = Combo(drives.ToArray(), Math.Max(0, drives.FindIndex(name => name.TrimEnd(':').Equals(ms.DiskDrive, StringComparison.OrdinalIgnoreCase))));
        Card(monitor, Ui.SymbolRegular.Server24, "监控的磁盘", "显示该磁盘的读写速度。", driveBox);
        var adapters = new List<(string Id, string Name)> { ("", "自动选择") };
        try { adapters.AddRange(NetworkInterface.GetAllNetworkInterfaces().Select(nic => (nic.Id, nic.Name))); } catch { }
        var netBox = Combo(adapters.Select(adapter => adapter.Name).ToArray(), Math.Max(0, adapters.FindIndex(adapter => adapter.Id == ms.NetworkAdapterId)));
        Card(monitor, Ui.SymbolRegular.Globe24, "监控的网卡", "自动选择会跟随当前联网的网卡。", netBox);
        Section(monitor, "外观与位置");
        string[] positions = { "BottomRight", "BottomLeft", "TopRight", "TopLeft" };
        var positionBox = Combo(new[] { "右下角", "左下角", "右上角", "左上角" }, Math.Max(0, Array.IndexOf(positions, ms.Position)));
        Card(monitor, Ui.SymbolRegular.Window24, "默认位置", "没有手动拖动过时停靠的屏幕角落；选择新角落会清除手动位置。", positionBox);
        Card(monitor, Ui.SymbolRegular.ArrowReset24, "恢复默认位置", "放弃手动拖动的位置，回到上面选的角落。", ActionButton("恢复", Ui.SymbolRegular.ArrowReset24, _host.ResetMonitorPosition));
        string[] orientations = { "Horizontal", "Vertical" };
        var orientationBox = Combo(new[] { "横向", "竖向" }, Math.Max(0, Array.IndexOf(orientations, ms.Orientation)));
        Card(monitor, Ui.SymbolRegular.Layer24, "排列方向", "", orientationBox);
        string[] densities = { "Compact", "Relaxed" };
        var densityBox = Combo(new[] { "紧凑", "宽松" }, Math.Max(0, Array.IndexOf(densities, ms.LayoutDensity)));
        Card(monitor, Ui.SymbolRegular.Apps24, "间距", "", densityBox);
        double[] opacities = { 0.7, 0.8, 0.85, 0.9, 1.0 };
        var opacityBox = Combo(new[] { "70%", "80%", "85%", "90%", "100%" }, Array.IndexOf(opacities, opacities.OrderBy(o => Math.Abs(o - ms.WindowOpacity)).First()));
        Card(monitor, Ui.SymbolRegular.DarkTheme24, "透明度", "", opacityBox);
        var gradient = Toggle(ms.UseColorGradient);
        Card(monitor, Ui.SymbolRegular.PaintBrush24, "按负载变色", "负载升高时数字由绿变黄再变红。", gradient);

        // ---------- command palette extension ----------
        BuildPalettePage(palette);

        // ---------- general ----------
        var theme = Combo(new[] { "跟随系统", "浅色", "深色" }, AppThemeSettings.Load().ThemeMode);
        Section(general, "外观", first: true);
        Card(general, Ui.SymbolRegular.DarkTheme24, "应用主题", "设置窗口、搜索栏和晾衣架统一使用。", theme);
        Section(general, "启动");
        var startup = Toggle(StartupService.IsEnabled());
        Card(general, Ui.SymbolRegular.Power24, "开机自动启动", "登录 Windows 后在托盘静默运行。", startup);
        Section(general, "关于");
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var aboutActions = new StackPanel { Orientation = Orientation.Horizontal };
        aboutActions.Children.Add(ActionButton("GitHub", Ui.SymbolRegular.Globe24, () => OpenUrl(AirTools.Core.AgentProtocol.Repository)));
        aboutActions.Children.Add(ActionButton("数据目录", Ui.SymbolRegular.FolderOpen24, () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LauncherSettings.DataDirectory) { UseShellExecute = true })));
        Card(general, Ui.SymbolRegular.Info24, $"AirTools {version?.Major}.{version?.Minor}.{version?.Build}", "Windows 剪切板历史、晾衣架与快速命令 · Apache-2.0 开源 · 数据保存在 " + LauncherSettings.DataDirectory, aboutActions);
        Card(general, Ui.SymbolRegular.Bug24, "反馈与更新", "遇到问题或想要新功能，欢迎到 GitHub 提交 Issue；新版本发布在 Releases。", ActionButton("打开 Issues", Ui.SymbolRegular.Open24, () => OpenUrl(AirTools.Core.AgentProtocol.Repository + "/issues")));

        // ---------- home (built last so it can link to every page) ----------
        BuildHome(home, () => (launch.Text, clip.Text, rackKey.Text));

        // ---------- tasks ----------
        BuildTasksPage(tasksPage);

        _saveGeneral = () =>
        {
            _draft.Hotkey = launch.Text.Trim(); _draft.ClipboardHotkey = clip.Text.Trim();
            _draft.RackEnabled = rackEnabled.IsChecked == true; _draft.RackCapacity = capacity.SelectedIndex + 3;
            _draft.RackHotkey = rackKey.Text.Trim(); _draft.RackSlotHotkeys = slots.Select(x => x.Text.Trim()).ToArray(); _draft.PasteCompatibilityFallback = fallback.IsChecked == true;
            _draft.ClipboardPaused = pause.IsChecked == true; _draft.ClipboardRecordImages = recImages.IsChecked == true; _draft.ClipboardRecordFiles = recFiles.IsChecked == true;
            _draft.ClipboardMaxItems = maxItemsValues[Math.Max(0, maxItems.SelectedIndex)]; _draft.ClipboardRetentionDays = retentionValues[Math.Max(0, retention.SelectedIndex)]; _draft.ClipboardImageBudgetMb = imageBudgetValues[Math.Max(0, imageBudget.SelectedIndex)];
            _draft.ClipboardModeKey = c.Text.Trim().ToLowerInvariant();
            if (_draft.ClipboardModeKey.Length != 1 || !char.IsAsciiLetter(_draft.ClipboardModeKey[0])) throw new ArgumentException("模式键必须是一个英文字母");
        };
        _saveTheme = () =>
        {
            var settings = AppThemeSettings.Load(); settings.ThemeMode = theme.SelectedIndex; settings.Save();
            // Monitor options live in their own file; reload so a position dragged meanwhile is not overwritten.
            var m = MonitorSettings.Load();
            if (m.Position != positions[Math.Max(0, positionBox.SelectedIndex)]) { m.Left = null; m.Top = null; }
            m.ShowCpu = showCpu.IsChecked == true; m.ShowMemory = showMem.IsChecked == true; m.ShowDisk = showDisk.IsChecked == true; m.ShowNetwork = showNet.IsChecked == true;
            m.UpdateIntervalMs = intervalValues[Math.Max(0, interval.SelectedIndex)];
            m.DiskDrive = drives[Math.Max(0, driveBox.SelectedIndex)].TrimEnd(':'); m.NetworkAdapterId = adapters[Math.Max(0, netBox.SelectedIndex)].Id;
            m.Position = positions[Math.Max(0, positionBox.SelectedIndex)]; m.Orientation = orientations[Math.Max(0, orientationBox.SelectedIndex)];
            m.LayoutDensity = densities[Math.Max(0, densityBox.SelectedIndex)]; m.WindowOpacity = opacities[Math.Max(0, opacityBox.SelectedIndex)]; m.UseColorGradient = gradient.IsChecked == true;
            _host.ApplyMonitorSettings(m);
            if (!StartupService.SetEnabled(startup.IsChecked == true)) throw new IOException("开机自启设置未能写入，请确认程序所在路径可用");
        };

        // ---------- save bar + toast overlay ----------
        var overlay = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(204 + 24, 0, 28, 16) };
        Grid.SetRow(overlay, 1); root.Children.Add(overlay);
        _toast = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), BorderThickness = new Thickness(1), Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 640, Margin = new Thickness(0, 0, 0, 72), RenderTransform = new TranslateTransform(0, 12) };
        _toast.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush"); _toast.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        _toast.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = .22 };
        _toast.Child = new DockPanel { Children = { _toastIcon, _toastText } };
        _toastText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        overlay.Children.Add(_toast);
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Animate(_toast, OpacityProperty, 0, 220); Animate((TranslateTransform)_toast.RenderTransform, TranslateTransform.YProperty, 12, 220); };

        var saveButton = ActionButton("保存", Ui.SymbolRegular.Checkmark24, Save, accent: true);
        var revert = ActionButton("放弃更改", null, Revert);
        var barContent = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right);
        actions.Children.Add(revert); actions.Children.Add(saveButton);
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(2, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, "SystemAccentColorPrimaryBrush");
        barContent.Children.Add(actions); barContent.Children.Add(dot); barContent.Children.Add(_saveText);
        _saveText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _saveBar = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 10, 10, 10), BorderThickness = new Thickness(1), Child = barContent, Visibility = Visibility.Collapsed, RenderTransform = new TranslateTransform(0, 80), MaxWidth = 880 };
        _saveBar.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush"); _saveBar.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        _saveBar.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = .25 };
        overlay.Children.Add(_saveBar);

        PreviewKeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Save(); } };
        Closing += (_, e) =>
        {
            if (!_dirty || _silent) return;
            var answer = MessageBox.Show(this, "有尚未保存的更改，是否保存？", "AirTools", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) e.Cancel = true;
            else if (answer == MessageBoxResult.Yes) { Save(); if (_dirty) e.Cancel = true; }
        };
        Loaded += (_, _) => { _loading = false; if (_pages.TryGetValue(_pendingPage ?? "主页", out var first) || _pages.TryGetValue("主页", out first)) SelectPage(first); };
    }

    // ================= save / dirty =================
    private void Save()
    {
        try
        {
            CommitTask(); _saveGeneral(); _host.ApplySettings(_draft); _saveTheme(); _host.RefreshTheme();
            SetDirty(false);
            if (!string.IsNullOrWhiteSpace(_host.HotkeyWarning)) Toast("已保存，但部分快捷键被占用：" + _host.HotkeyWarning, Ui.InfoBarSeverity.Warning);
            else Toast("已保存，快捷键立即生效", Ui.InfoBarSeverity.Success);
        }
        catch (Exception ex) { Toast(ex.Message, Ui.InfoBarSeverity.Error); }
    }

    private void Revert()
    {
        SetDirty(false);
        var bounds = new Rect(Left, Top, Width, Height); var state = WindowState;
        Close();
        _host.ShowSettings(bounds, state);
    }

    private void MarkDirty() { if (_loading) return; _changes++; SetDirty(true); }

    private void SetDirty(bool dirty)
    {
        bool was = _dirty; _dirty = dirty;
        if (!dirty) _changes = 0;
        _saveText.Text = _changes <= 1 ? "有 1 项未保存的更改" : $"有 {_changes} 处未保存的更改";
        if (dirty == was) return;
        var shift = (TranslateTransform)_saveBar.RenderTransform;
        if (dirty)
        {
            _saveBar.Visibility = Visibility.Visible;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(80, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = .3, EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            var down = new DoubleAnimation(80, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            down.Completed += (_, _) => { if (!_dirty) _saveBar.Visibility = Visibility.Collapsed; };
            shift.BeginAnimation(TranslateTransform.YProperty, down);
        }
    }

    private void Toast(string message, Ui.InfoBarSeverity severity)
    {
        _toastText.Text = message;
        (_toastIcon.Symbol, var brush) = severity switch
        {
            Ui.InfoBarSeverity.Success => (Ui.SymbolRegular.CheckmarkCircle24, "SystemFillColorSuccessBrush"),
            Ui.InfoBarSeverity.Warning => (Ui.SymbolRegular.Warning24, "SystemFillColorCautionBrush"),
            Ui.InfoBarSeverity.Error => (Ui.SymbolRegular.ErrorCircle24, "SystemFillColorCriticalBrush"),
            _ => (Ui.SymbolRegular.Info24, "SystemAccentColorPrimaryBrush"),
        };
        _toastIcon.SetResourceReference(ForegroundProperty, brush);
        Animate(_toast, OpacityProperty, 1, 180); Animate((TranslateTransform)_toast.RenderTransform, TranslateTransform.YProperty, 0, 260);
        _toastTimer.Stop(); _toastTimer.Interval = TimeSpan.FromSeconds(severity == Ui.InfoBarSeverity.Success ? 2.5 : 6); _toastTimer.Start();
    }

    // ================= navigation =================
    private void AddHeader(string text) => _navigation.MenuItems.Add(new Ui.NavigationViewItemHeader { Text = text, Margin = new Thickness(0, _navigation.MenuItems.Count == 0 ? 4 : 14, 0, 2) });

    private void AddPage(string title, Ui.SymbolRegular icon, Panel content)
    {
        UIElement page = content is Grid ? content : new ScrollViewer
        {
            Content = new Border { Child = content, MaxWidth = 900, Padding = new Thickness(24, 2, 24, 72) },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var item = new Ui.NavigationViewItem { Content = title, Icon = new Ui.SymbolIcon(icon), Tag = page };
        item.Click += (_, _) => SelectPage(item);
        _navigation.MenuItems.Add(item); _pages[title] = item;
    }

    /// <summary>Switches to a page by its navigation title; before the window has loaded it is remembered.</summary>
    public void Navigate(string title)
    {
        if (!IsLoaded) { _pendingPage = title; return; }
        if (_pages.TryGetValue(title, out var item)) SelectPage(item);
    }

    private void SelectPage(Ui.NavigationViewItem item)
    {
        if (item.Tag is not FrameworkElement page) return;
        foreach (var other in _navigation.MenuItems.OfType<Ui.NavigationViewItem>())
            other.SetCurrentValue(Ui.NavigationViewItem.IsActiveProperty, other == item);
        _navigation.ReplaceContent(page, null);
        if (page is ScrollViewer scroll) scroll.ScrollToTop();
        // Page entrance: fade + gentle rise.
        var shift = page.RenderTransform as TranslateTransform ?? new TranslateTransform(); page.RenderTransform = shift;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    // ================= home =================
    private void BuildHome(StackPanel home, Func<(string Search, string History, string Rack)> keys)
    {
        var hero = new Border { CornerRadius = new CornerRadius(12), Height = 124, Margin = new Thickness(0, 6, 0, 14), ClipToBounds = true, Background = Gradient("#3D4FD8", "#B05CE6", "#E2609E") };
        var heroGrid = new Grid(); hero.Child = heroGrid;
        heroGrid.Children.Add(Bubbles());
        var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 0, 36, 0) };
        heroText.Children.Add(new TextBlock { Text = "AirTools", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        heroText.Children.Add(new TextBlock { Text = "复制、搜索、启动 —— 让常用操作离你只有一次点击。", FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        heroGrid.Children.Add(heroText);
        home.Children.Add(hero);

        Section(home, "工具", first: true);
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-6, 0, -6, 0) };
        home.Children.Add(grid);
        var (s, h, r) = keys();
        Tile(grid, "晾衣架", "最近复制的文字，悬停展开，点击粘贴", "\uE77F", "#6A5AE0", "#E2609E", r);
        Tile(grid, "剪切板历史", "搜索、置顶与管理全部记录", "\uE81C", "#1F6FE5", "#29B6D8", h);
        Tile(grid, "搜索启动器", "快速命令与剪切板一处搜索", "\uE721", "#3949D6", "#8A5CF6", s);
        Tile(grid, "快速启动", $"{_draft.Tasks.Count} 个任务 · 程序、终端、脚本与 SSH 抓包", "\uE768", "#E8743B", "#F2B33D", _draft.Tasks.FirstOrDefault(t => t.Hotkey.Length > 0)?.Hotkey ?? "");
        Tile(grid, "系统监控", "CPU、内存、磁盘与网络速率", "\uE9D9", "#14956A", "#3CC6A4", "");
        Tile(grid, "命令面板", "在 PowerToys 命令面板中搜索命令与历史", "", "#2B6CB0", "#63B3ED", "");
        Tile(grid, "常规","主题、开机启动与数据目录", "\uE713", "#5B6476", "#8C96A8", "");

        Section(home, "小提示");
        var tips = new StackPanel();
        tips.Children.Add(Tip("把鼠标移到屏幕右下角的晾衣架按钮上方，衣物会沿弧线展开；单击按钮可以收起整个衣架。"));
        tips.Children.Add(Tip("在搜索栏的空白输入框里键入模式键（默认 c），即可切换到剪切板搜索。"));
        tips.Children.Add(Tip("快捷键卡片上的 × 会禁用该快捷键；点击按键区域后按 Esc 取消录制。"));
        home.Children.Add(tips);
    }

    private void Tile(UniformGrid grid, string page, string description, string glyph, string from, string to, string gesture)
    {
        var tile = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(5), Cursor = Cursors.Hand, MinHeight = 112, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
        tile.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        tile.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var stack = new StackPanel(); tile.Child = stack;
        stack.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Background = Gradient(from, to), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        stack.Children.Add(Text(page, 15, FontWeights.SemiBold, "TextFillColorPrimaryBrush", new Thickness(0, 10, 0, 2)));
        stack.Children.Add(Text(description, 12, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0, 0, 0, 8)));
        if (gesture.Length > 0) stack.Children.Add(Keycaps(gesture, small: true));
        var scale = (ScaleTransform)tile.RenderTransform;
        tile.MouseEnter += (_, _) => { Animate(scale, ScaleTransform.ScaleXProperty, 1.02, 160); Animate(scale, ScaleTransform.ScaleYProperty, 1.02, 160); tile.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorSecondaryBrush"); };
        tile.MouseLeave += (_, _) => { Animate(scale, ScaleTransform.ScaleXProperty, 1, 200); Animate(scale, ScaleTransform.ScaleYProperty, 1, 200); tile.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush"); };
        tile.MouseLeftButtonUp += (_, _) => Navigate(page);
        grid.Children.Add(tile);
    }

    private static UIElement Tip(string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var icon = new TextBlock { Text = "\uEA80", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14, Margin = new Thickness(2, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "SystemAccentColorPrimaryBrush");
        row.Children.Add(icon);
        row.Children.Add(Text(text, 13, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0)));
        return row;
    }

    private static void OpenUrl(string url) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    // ================= command palette extension =================
    private void BuildPalettePage(StackPanel page)
    {
        var statusText = Text("正在检查…", 12, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0));
        var progress = new Ui.ProgressRing { IsIndeterminate = true, Width = 18, Height = 18, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 10, 0) };
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        statusRow.Children.Add(progress); statusRow.Children.Add(statusText);

        Ui.Button install = null!, uninstall = null!;
        var busy = false;
        async Task RunAsync(string working, Func<Task<string>> work)
        {
            if (busy) return; busy = true;
            install.IsEnabled = uninstall.IsEnabled = false; progress.Visibility = Visibility.Visible; statusText.Text = working;
            try { var done = await work(); Toast(done, Ui.InfoBarSeverity.Success); }
            catch (Exception ex) { Toast(ex.Message, Ui.InfoBarSeverity.Error); }
            finally { busy = false; progress.Visibility = Visibility.Collapsed; await RefreshAsync(); }
        }
        async Task RefreshAsync()
        {
            ExtensionStatus status;
            try { status = await ExtensionInstaller.GetStatusAsync(); }
            catch (Exception ex) { statusText.Text = "无法读取状态：" + ex.Message; install.IsEnabled = true; return; }
            string state = !status.HostPresent ? "未检测到 PowerToys 命令面板（请先安装 PowerToys 或 Microsoft.CommandPalette）"
                : status.Installed ? $"已安装 · 版本 {status.ExtensionVersion}"
                : !status.DeveloperMode ? "未安装 · 需要先开启 Windows 开发人员模式"
                : "未安装";
            statusText.Text = state;
            install.Content = status.Installed ? "重新安装" : "安装到命令面板";
            install.IsEnabled = status.HostPresent; uninstall.IsEnabled = status.Installed;
        }

        install = ActionButton("安装到命令面板", Ui.SymbolRegular.ArrowDownload24, () => _ = RunAsync("正在安装…", () => ExtensionInstaller.InstallAsync(message => Dispatcher.Invoke(() => statusText.Text = message))), accent: true);
        uninstall = ActionButton("卸载", Ui.SymbolRegular.Delete24, () => _ = RunAsync("正在卸载…", () => ExtensionInstaller.UninstallAsync()));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(install); buttons.Children.Add(uninstall);

        var statusCard = new Ui.CardControl { Header = new StackPanel { Children = { Text("扩展状态", 14, FontWeights.SemiBold, "TextFillColorPrimaryBrush", new Thickness(0)), new Border { Height = 4 }, statusRow } },
            Content = buttons, Icon = new Ui.SymbolIcon(Ui.SymbolRegular.PuzzlePiece24) { FontSize = 22 }, Margin = new Thickness(0, 0, 0, 3), Padding = new Thickness(14, 10, 14, 10), MinHeight = 66 };
        page.Children.Add(statusCard);
        _ = RefreshAsync();

        Section(page, "使用");
        Card(page, Ui.SymbolRegular.Search24, "在命令面板中搜索", "安装后，在命令面板（默认 Win+Alt+空格）输入 AirTools，选择“快速命令”或“剪切板”。", new Border());
        Card(page, Ui.SymbolRegular.Info24, "需要满足", "已安装 PowerToys 命令面板；Windows 已开启“开发人员模式”；AirTools 正在运行（扩展只是入口，不保存数据）。", new Border());
        Card(page, Ui.SymbolRegular.Settings24, "设置在哪里", "历史条数、保留时长、快捷键、任务增删改，都在 AirTools 设置里配置，扩展本身没有设置。", ActionButton("了解 PowerToys", Ui.SymbolRegular.Open24, () => OpenUrl("https://learn.microsoft.com/windows/powertoys/command-palette/overview")));
    }

    // ================= tasks =================
    private void BuildTasksPage(Grid page)
    {
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
        page.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel { Margin = new Thickness(20, 4, 8, 16) };
        page.Children.Add(left);
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) }; DockPanel.SetDock(header, Dock.Top); left.Children.Add(header);
        header.Children.Add(Text("快速启动", 22, FontWeights.SemiBold, "TextFillColorPrimaryBrush", new Thickness(0, 4, 0, 2)));
        header.Children.Add(Text("在搜索栏中输入名称或别名运行，也可以绑定独立快捷键。", 12, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0)));
        var actions = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false }; DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions);
        var add = ActionButton("新增任务", Ui.SymbolRegular.Add24, () => { CommitTask(); var task = new LaunchTask(); _draft.Tasks.Add(task); Reload(task); MarkDirty(); }, accent: true, margin: new Thickness(0));
        actions.Children.Add(add);
        var remove = IconButton(Ui.SymbolRegular.Delete24, "删除所选任务", () => { if (_tasks.SelectedItem is LaunchTask task && MessageBox.Show(this, "删除任务「" + task.Name + "」？", "删除任务", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { _commitFields = null; _draft.Tasks.Remove(task); Reload(_draft.Tasks.FirstOrDefault()); MarkDirty(); } });
        var duplicate = IconButton(Ui.SymbolRegular.Copy24, "复制所选任务", () => { CommitTask(); if (_tasks.SelectedItem is LaunchTask task) { var copy = JsonSerializer.Deserialize<LaunchTask>(JsonSerializer.Serialize(task))!; copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " 副本"; copy.Hotkey = ""; _draft.Tasks.Add(copy); Reload(copy); MarkDirty(); } });
        DockPanel.SetDock(remove, Dock.Right); DockPanel.SetDock(duplicate, Dock.Right);
        actions.Children.Add(remove); actions.Children.Add(duplicate);
        _tasks.ItemContainerStyle = TaskItemStyle();
        var listCard = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = _tasks };
        listCard.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        listCard.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        left.Children.Add(listCard);
        _tasks.ItemTemplate = TaskTemplate();
        ScrollViewer.SetHorizontalScrollBarVisibility(_tasks, ScrollBarVisibility.Disabled);

        var scroll = new ScrollViewer { Content = new Border { Child = _form, MaxWidth = 700, Padding = new Thickness(8, 8, 20, 72), HorizontalAlignment = HorizontalAlignment.Left }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); page.Children.Add(scroll);
        _tasks.SelectionChanged += (_, e) =>
        {
            if (e.RemovedItems.Count > 0) { try { CommitTask(); } catch (Exception ex) { Toast(ex.Message, Ui.InfoBarSeverity.Error); } }
            if (_tasks.SelectedItem is LaunchTask task) Edit(task); else ShowNoTask();
        };
        Reload(_draft.Tasks.FirstOrDefault());
    }

    private void ShowNoTask()
    {
        _form.Children.Clear(); _commitFields = null;
        var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 120, 0, 0) };
        empty.Children.Add(new Ui.SymbolIcon(Ui.SymbolRegular.Rocket24) { FontSize = 48, Opacity = .5, HorizontalAlignment = HorizontalAlignment.Center });
        empty.Children.Add(Text("还没有任务", 16, FontWeights.SemiBold, "TextFillColorPrimaryBrush", new Thickness(0, 14, 0, 4)));
        empty.Children.Add(Text("点击左下角“新增”，添加一个程序、终端命令或脚本。", 12, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0)));
        _form.Children.Add(empty);
    }

    private static DataTemplate TaskTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(MarginProperty, new Thickness(4, 6, 4, 6));
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(WidthProperty, 34.0); badge.SetValue(HeightProperty, 34.0); badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        badge.SetValue(Border.BackgroundProperty, Gradient("#E8743B", "#F2B33D")); badge.SetValue(MarginProperty, new Thickness(0, 0, 12, 0));
        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(LaunchTask.Icon)));
        icon.SetValue(TextBlock.ForegroundProperty, Brushes.White); icon.SetValue(TextBlock.FontSizeProperty, 15.0);
        icon.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); icon.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        badge.AppendChild(icon); row.AppendChild(badge);
        var texts = new FrameworkElementFactory(typeof(StackPanel)); texts.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding(nameof(LaunchTask.Name)));
        name.SetValue(TextBlock.FontSizeProperty, 13.5); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var kind = new FrameworkElementFactory(typeof(TextBlock)); kind.SetBinding(TextBlock.TextProperty, new Binding(nameof(LaunchTask.Kind)) { Converter = new KindLabel() });
        kind.SetValue(TextBlock.FontSizeProperty, 11.5); kind.SetValue(MarginProperty, new Thickness(0, 2, 0, 0));
        kind.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        texts.AppendChild(name); texts.AppendChild(kind); row.AppendChild(texts);
        return new DataTemplate { VisualTree = row };
    }

    private sealed class KindLabel : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is LaunchKind kind ? KindName(kind) : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    private static string KindName(LaunchKind kind) => kind switch { LaunchKind.Program => "程序", LaunchKind.Terminal => "终端命令", LaunchKind.PowerShell => "PowerShell 脚本", _ => "SSH 远程抓包" };
    private static string PolicyName(TerminalPolicy policy) => policy switch { TerminalPolicy.Close => "运行结束后关闭", TerminalPolicy.KeepOnFailure => "失败时保留窗口", _ => "始终保留窗口" };

    private void CommitTask() => _commitFields?.Invoke();
    private void Reload(LaunchTask? selected) { _tasks.ItemsSource = null; _tasks.ItemsSource = _draft.Tasks; _tasks.SelectedItem = selected; if (selected == null) ShowNoTask(); }

    private void Edit(LaunchTask task)
    {
        bool wasLoading = _loading; _loading = true;
        _form.Children.Clear();
        var title = Text(task.Name, 18, FontWeights.SemiBold, "TextFillColorPrimaryBrush", new Thickness(0, 0, 0, 2));
        _form.Children.Add(title);
        _form.Children.Add(Text("修改后点击底部“保存”生效。", 12, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0, 0, 0, 10)));

        var basics = Group(_form, "基本信息");
        var name = Field(basics, "名称", task.Name);
        name.TextChanged += (_, _) => title.Text = name.Text.Length > 0 ? name.Text : "未命名任务";
        var row = TwoColumns(basics);
        var alias = Field(row.Left, "搜索别名", task.Alias, "空格分隔多个别名");
        var icon = Field(row.Right, "图标字符", task.Icon, "例如 ▶ ◉ ⚡");
        basics.Children.Add(Label("类型"));
        var kinds = Enum.GetValues<LaunchKind>();
        var kind = Combo(kinds.Select(KindName).ToArray(), Array.IndexOf(kinds, task.Kind)); kind.HorizontalAlignment = HorizontalAlignment.Left; kind.MinWidth = 220;
        basics.Children.Add(kind);
        var hotkey = Recorder(basics, null, "独立快捷键", "可留空", task.Hotkey, nested: true);

        var run = Group(_form, "运行");
        var exe = Field(run, "程序路径", task.Executable);
        var dir = Field(run, "工作目录", task.WorkingDirectory, "留空使用程序所在目录");
        var args = Field(run, "参数 · 每行一个，整行作为一个参数，不需要额外引号", string.Join(Environment.NewLine, task.Arguments), multiline: true);
        var script = Field(run, "PowerShell 脚本", task.Script, multiline: true);
        var policyLabel = Label("终端窗口"); run.Children.Add(policyLabel);
        var policies = Enum.GetValues<TerminalPolicy>();
        var policy = Combo(policies.Select(PolicyName).ToArray(), Array.IndexOf(policies, task.TerminalPolicy)); policy.HorizontalAlignment = HorizontalAlignment.Left; policy.MinWidth = 220;
        run.Children.Add(policy);

        var ssh = Group(_form, "SSH 远程抓包");
        var hostRow = TwoColumns(ssh, 3, 1);
        var host = Field(hostRow.Left, "主机", task.Host); var port = Field(hostRow.Right, "端口", task.Port.ToString());
        var user = Field(ssh, "用户名", task.Username);
        ssh.Children.Add(Label("密码 / 私钥口令（留空保留原值）"));
        var password = new Ui.PasswordBox { PlaceholderText = task.EncryptedPassword.Length > 0 ? "已保存 ·••••••" : "未保存" }; ssh.Children.Add(password);
        var clearPassword = new CheckBox { Content = "清除已保存的密码", Margin = new Thickness(0, 8, 0, 0) }; ssh.Children.Add(clearPassword);
        var key = Field(ssh, "私钥文件", task.PrivateKey, "使用密码认证时留空");
        var ifaceRow = TwoColumns(ssh);
        var iface = Field(ifaceRow.Left, "抓包接口", task.Interface); var tcpdump = Field(ifaceRow.Right, "远端 tcpdump 路径", task.Tcpdump);
        var filter = Field(ssh, "抓包过滤器", task.CaptureFilter, "默认排除 SSH 自身流量，可清空");
        var extra = Field(ssh, "远端额外参数（Shell 参数，仅填写可信内容）", task.ExtraCaptureArguments);
        var fingerprint = Field(ssh, "已信任主机指纹", task.Fingerprint, "更换服务器时可清空");
        var testSsh = ActionButton("测试 SSH 与 tcpdump", Ui.SymbolRegular.PlugConnected24, async () =>
        {
            try { CommitTask(); Toast("正在连接…", Ui.InfoBarSeverity.Informational); await _host.Runner.TestAsync(task); fingerprint.Text = task.Fingerprint; Toast("SSH 与 tcpdump 检查通过；抓包权限将在启动时检查", Ui.InfoBarSeverity.Success); }
            catch (Exception ex) { Toast(ex.Message, Ui.InfoBarSeverity.Error); }
        }, margin: new Thickness(0, 16, 0, 0));
        ssh.Children.Add(testSsh);

        void Visibility()
        {
            var k = kinds[Math.Max(0, kind.SelectedIndex)];
            Show(exe, k != LaunchKind.PowerShell); Show(args, k is LaunchKind.Program or LaunchKind.Terminal); Show(script, k == LaunchKind.PowerShell);
            Show(dir, k != LaunchKind.SshCapture); Show(policyLabel, k is LaunchKind.Terminal or LaunchKind.PowerShell); Show(policy, k is LaunchKind.Terminal or LaunchKind.PowerShell);
            ((Border)ssh.Parent).Visibility = k == LaunchKind.SshCapture ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            SetLabel(exe, k == LaunchKind.SshCapture ? "Wireshark 路径" : "程序路径");
        }
        kind.SelectionChanged += (_, _) => Visibility(); Visibility();
        password.PasswordChanged += (_, _) => MarkDirty(); clearPassword.Checked += (_, _) => MarkDirty();

        _commitFields = () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("任务名称不能为空");
            task.Name = name.Text.Trim(); task.Alias = alias.Text; task.Icon = icon.Text; task.Kind = kinds[Math.Max(0, kind.SelectedIndex)];
            task.Hotkey = hotkey.Text.Trim(); task.Executable = exe.Text.Trim(); task.WorkingDirectory = dir.Text.Trim();
            task.Arguments = args.Text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries); task.Script = script.Text; task.TerminalPolicy = policies[Math.Max(0, policy.SelectedIndex)];
            if (!int.TryParse(port.Text, out var parsed) || parsed is < 1 or > 65535) throw new ArgumentException("SSH 端口应为 1–65535");
            if (task.Host != host.Text.Trim() || task.Port != parsed) task.Fingerprint = ""; else task.Fingerprint = fingerprint.Text.Trim();
            task.Host = host.Text.Trim(); task.Port = parsed; task.Username = user.Text.Trim(); task.PrivateKey = key.Text.Trim(); task.Interface = iface.Text.Trim(); task.Tcpdump = tcpdump.Text.Trim(); task.CaptureFilter = filter.Text.Trim(); task.ExtraCaptureArguments = extra.Text;
            if (clearPassword.IsChecked == true) task.EncryptedPassword = "";
            if (password.Password.Length > 0) task.EncryptedPassword = TaskRunner.Protect(password.Password);
            password.Clear(); clearPassword.IsChecked = false;
            _tasks.Items.Refresh();
        };

        var commands = new WrapPanel { Margin = new Thickness(-4, 8, 0, 0) }; _form.Children.Add(commands);
        commands.Children.Add(ActionButton("保存并运行", Ui.SymbolRegular.Play24, () =>
        {
            try { CommitTask(); _saveGeneral(); _host.ApplySettings(_draft); SetDirty(false); _ = _host.Runner.RunAsync(_host.Settings.Tasks.First(x => x.Id == task.Id)); Toast("已保存并启动「" + task.Name + "」", Ui.InfoBarSeverity.Success); }
            catch (Exception ex) { Toast(ex.Message, Ui.InfoBarSeverity.Error); }
        }, accent: true));
        commands.Children.Add(ActionButton("结束运行", Ui.SymbolRegular.Stop24, () =>
        {
            if (_host.Runner.Stop(task.Id)) Toast("已结束「" + task.Name + "」", Ui.InfoBarSeverity.Success);
            else Toast("「" + task.Name + "」当前没有在运行", Ui.InfoBarSeverity.Informational);
        }));
        commands.Children.Add(ActionButton("查看上次日志", Ui.SymbolRegular.DocumentText24, () =>
        {
            var log = TaskRunner.LogPath(task.Id); var exit = TaskRunner.ResultPath(task.Id);
            MessageBox.Show(this, (File.Exists(exit) ? "退出码：" + File.ReadAllText(exit) + "\n\n" : "") + (File.Exists(log) ? File.ReadAllText(log) : "暂无运行日志"), task.Name);
        }));
        _loading = wasLoading;
    }

    private static void Show(FrameworkElement element, bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        element.Visibility = v;
        if (element.Tag is FrameworkElement label) label.Visibility = v;
    }
    private static void SetLabel(FrameworkElement element, string text) { if (element.Tag is TextBlock label) label.Text = text; }

    // ================= building blocks =================
    private static StackPanel Page(string title, string description)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text(title, 22, FontWeights.SemiBold, "TextFillColorPrimaryBrush", new Thickness(0, 8, 0, 3)));
        panel.Children.Add(Text(description, 12.5, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0, 0, 0, 8)));
        return panel;
    }

    /// <summary>Tool page with a PowerToys-like hero (gradient + preview) and three feature callouts.</summary>
    private static StackPanel ToolPage(string title, string description, string from, string to, UIElement mock, params (string Glyph, string Title, string Text)[] features)
    {
        var panel = new StackPanel();
        var hero = new Border { CornerRadius = new CornerRadius(12, 12, 0, 0), Height = 150, Margin = new Thickness(0, 6, 0, 0), ClipToBounds = true, Background = Gradient(from, to) };
        var grid = new Grid(); hero.Child = grid;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var bubbles = Bubbles(); Grid.SetColumnSpan(bubbles, 2); grid.Children.Add(bubbles);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        text.Children.Add(new TextBlock { Text = description, FontSize = 12.5, LineHeight = 19, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 6, 0, 0) });
        grid.Children.Add(text);
        if (mock is FrameworkElement element) { element.Margin = new Thickness(0); var view = new Viewbox { Child = element, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxHeight = 128, Margin = new Thickness(8, 16, 24, 0), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(view, 1); grid.Children.Add(view); }
        panel.Children.Add(hero);

        var strip = new Border { CornerRadius = new CornerRadius(0, 0, 12, 12), BorderThickness = new Thickness(1, 0, 1, 1), Padding = new Thickness(10, 11, 10, 11), Margin = new Thickness(0, 0, 0, 14) };
        strip.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        strip.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var row = new UniformGrid { Columns = features.Length, Rows = 1 }; strip.Child = row;
        foreach (var (glyph, name, body) in features)
        {
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            cell.Children.Add(icon);
            var t = Text(name, 13.5, FontWeights.SemiBold, "TextFillColorPrimaryBrush", new Thickness(0, 0, 0, 2)); t.TextAlignment = TextAlignment.Center; cell.Children.Add(t);
            var b = Text(body, 12, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0)); b.TextAlignment = TextAlignment.Center; cell.Children.Add(b);
            row.Children.Add(cell);
        }
        panel.Children.Add(strip);
        return panel;
    }

    private static void Section(Panel panel, string title, bool first = false)
        => panel.Children.Add(Text(title, 14, FontWeights.SemiBold, "TextFillColorPrimaryBrush", new Thickness(2, first ? 4 : 14, 0, 6)));

    private static void Card(Panel panel, Ui.SymbolRegular? icon, string title, string description, UIElement control, bool prominent = false)
    {
        var header = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        header.Children.Add(Text(title, prominent ? 14 : 13.5, prominent ? FontWeights.SemiBold : FontWeights.Normal, "TextFillColorPrimaryBrush", new Thickness(0)));
        if (description.Length > 0) header.Children.Add(Text(description, 12, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0, 1, 0, 0)));
        var card = new Ui.CardControl { Header = header, Content = control, Margin = new Thickness(0, 0, 0, 3), Padding = new Thickness(14, 8, 14, 8), MinHeight = prominent ? 62 : 52 };
        if (icon is Ui.SymbolRegular symbol) card.Icon = new Ui.SymbolIcon(symbol) { FontSize = prominent ? 22 : 18 };
        panel.Children.Add(card);
    }

    private static StackPanel Expander(Panel panel, Ui.SymbolRegular icon, string title, string description)
    {
        var header = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(Text(title, 14, FontWeights.Normal, "TextFillColorPrimaryBrush", new Thickness(0)));
        header.Children.Add(Text(description, 12, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0, 3, 0, 0)));
        var body = new StackPanel();
        panel.Children.Add(new Ui.CardExpander { Header = header, Icon = new Ui.SymbolIcon(icon) { FontSize = 18 }, Content = body, Margin = new Thickness(0, 0, 0, 3), Padding = new Thickness(14, 8, 14, 8), ContentPadding = new Thickness(48, 8, 14, 12) });
        return body;
    }

    private StackPanel Group(Panel panel, string title)
    {
        var body = new StackPanel();
        var border = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16, 10, 16, 12), Margin = new Thickness(0, 0, 0, 8), Child = body };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        body.Children.Add(Text(title, 14, FontWeights.SemiBold, "TextFillColorPrimaryBrush", new Thickness(0, 0, 0, 2)));
        panel.Children.Add(border);
        return body;
    }

    private static (StackPanel Left, StackPanel Right) TwoColumns(Panel panel, double left = 1, double right = 1)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(left, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(right, GridUnitType.Star) });
        var a = new StackPanel(); var b = new StackPanel(); Grid.SetColumn(b, 2);
        grid.Children.Add(a); grid.Children.Add(b); panel.Children.Add(grid);
        return (a, b);
    }

    private static TextBlock Label(string text) => Text(text, 12.5, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(0, 8, 0, 3));

    private Ui.TextBox Field(Panel panel, string label, string value, string placeholder = "", bool multiline = false)
    {
        var caption = Label(label); panel.Children.Add(caption);
        var box = new Ui.TextBox
        {
            Text = value, PlaceholderText = placeholder, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 72 : 32, MaxHeight = multiline ? 160 : 36, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            FontFamily = multiline ? new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI") : FontFamily, Tag = caption,
        };
        box.TextChanged += (_, _) => MarkDirty();
        panel.Children.Add(box); return box;
    }

    private Ui.TextBox MultiLine(Panel panel, string value, string placeholder)
    {
        var box = new Ui.TextBox { Text = value, PlaceholderText = placeholder, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 72, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI") };
        box.TextChanged += (_, _) => MarkDirty();
        panel.Children.Add(box); return box;
    }

    private Ui.TextBox ModeKey(string value)
    {
        var box = new Ui.TextBox { Text = value, MaxLength = 1, Width = 56, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.SemiBold, CharacterCasing = CharacterCasing.Lower, ClearButtonEnabled = false };
        box.TextChanged += (_, _) => MarkDirty();
        return box;
    }

    private Ui.ToggleSwitch Toggle(bool value)
    {
        var toggle = new Ui.ToggleSwitch { IsChecked = value, OnContent = "开", OffContent = "关" };
        toggle.Checked += (_, _) => MarkDirty(); toggle.Unchecked += (_, _) => MarkDirty();
        return toggle;
    }

    private ComboBox Combo(string[] items, int selected)
    {
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = Math.Clamp(selected, 0, items.Length - 1), MinWidth = 160 };
        combo.SelectionChanged += (_, _) => MarkDirty();
        return combo;
    }

    /// <summary>Drop-down over integer presets; a saved value outside the presets is kept as an extra entry.</summary>
    private (ComboBox Box, int[] Values) IntCombo(int[] presets, int current, Func<int, string> label)
    {
        var values = presets.Contains(current) ? presets : presets.Append(current).OrderBy(n => n).ToArray();
        return (Combo(values.Select(label).ToArray(), Array.IndexOf(values, current)), values);
    }

    private static Ui.Button ActionButton(string title, Ui.SymbolRegular? icon, Action action, bool accent = false, bool stretch = false, Thickness? margin = null)
    {
        var button = new Ui.Button { Content = title, Padding = new Thickness(14, 6, 14, 6), Margin = margin ?? new Thickness(4), MinWidth = stretch ? 0 : 96, MinHeight = 32 };
        if (stretch) button.HorizontalAlignment = HorizontalAlignment.Stretch; else if (margin != null) button.HorizontalAlignment = HorizontalAlignment.Left;
        if (icon is Ui.SymbolRegular symbol) button.Icon = new Ui.SymbolIcon(symbol);
        if (accent) button.Appearance = Ui.ControlAppearance.Primary;
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "AirTools"); } };
        return button;
    }

    private static Ui.Button IconButton(Ui.SymbolRegular icon, string tooltip, Action action)
    {
        var button = new Ui.Button { Content = new Ui.SymbolIcon(icon) { FontSize = 16 }, ToolTip = tooltip, Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(6, 0, 0, 0) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "AirTools"); } };
        return button;
    }

    /// <summary>Fluent list selection: subtle fill + accent rail instead of a solid blue block.</summary>
    private static Style TaskItemStyle()
    {
        var template = new ControlTemplate(typeof(ListBoxItem));
        var grid = new FrameworkElementFactory(typeof(Grid)); grid.SetValue(MarginProperty, new Thickness(0, 1, 0, 1));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); bd.SetValue(Border.BackgroundProperty, Brushes.Transparent); bd.SetValue(Border.PaddingProperty, new Thickness(6, 2, 6, 2));
        bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var rail = new FrameworkElementFactory(typeof(Border), "Rail");
        rail.SetValue(WidthProperty, 3.0); rail.SetValue(HeightProperty, 16.0); rail.SetValue(Border.CornerRadiusProperty, new CornerRadius(1.5));
        rail.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left); rail.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        rail.SetResourceReference(Border.BackgroundProperty, "SystemAccentColorPrimaryBrush"); rail.SetValue(OpacityProperty, 0.0);
        grid.AppendChild(bd); grid.AppendChild(rail);
        template.VisualTree = grid;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SubtleFillColorSecondaryBrush"), "Bd"));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SubtleFillColorSecondaryBrush"), "Bd"));
        selected.Setters.Add(new Setter(OpacityProperty, 1.0, "Rail"));
        template.Triggers.Add(hover); template.Triggers.Add(selected);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        return style;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, string brush, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = margin };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    // ================= shortcut recorder =================
    private sealed class Shortcut { public string Text = ""; }

    private Shortcut Recorder(Panel panel, Ui.SymbolRegular? icon, string title, string description, string value, bool prominent = false, bool nested = false)
    {
        var shortcut = new Shortcut { Text = value };
        var keys = new ContentControl { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        var record = new Ui.Button { Appearance = Ui.ControlAppearance.Transparent, Padding = new Thickness(6, 4, 6, 4), MinHeight = 30, ToolTip = "点击后按下新的组合键 · Esc 取消 · Backspace 清除", Content = keys, BorderThickness = new Thickness(1) };
        record.SetResourceReference(Control.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var clear = new Ui.Button { Content = new Ui.SymbolIcon(Ui.SymbolRegular.Dismiss16), Appearance = Ui.ControlAppearance.Transparent, ToolTip = "清除并禁用此快捷键", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(record, "录制" + title);
        System.Windows.Automation.AutomationProperties.SetName(clear, "清除" + title);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(record); controls.Children.Add(clear);

        bool recording = false;
        Storyboard? pulse = null;
        void Refresh()
        {
            keys.Content = recording ? RecordingHint() : Keycaps(shortcut.Text);
            clear.Visibility = shortcut.Text.Length == 0 || recording ? Visibility.Hidden : Visibility.Visible;
        }
        void Finish()
        {
            if (!recording) return;
            recording = false; pulse?.Stop(); record.Opacity = 1;
            record.SetResourceReference(Control.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            Refresh(); _host.ResumeHotkeys();
        }
        void Set(string text) { if (text == shortcut.Text) return; shortcut.Text = text; MarkDirty(); }
        Refresh();
        clear.Click += (_, _) => { Finish(); Set(""); Refresh(); };
        record.Click += (_, _) =>
        {
            if (recording) { Finish(); return; }
            _host.SuspendHotkeys(); recording = true; Refresh(); record.Focus();
            record.SetResourceReference(Control.BorderBrushProperty, "SystemAccentColorPrimaryBrush");
            var anim = new DoubleAnimation(1, .55, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            pulse = new Storyboard { Children = { anim } }; Storyboard.SetTarget(anim, keys); Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
            pulse.Begin(); pulse.Completed += (_, _) => keys.Opacity = 1;
        };
        record.LostKeyboardFocus += (_, _) => { Finish(); keys.Opacity = 1; };
        Closed += (_, _) => Finish();
        record.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { Finish(); keys.Opacity = 1; return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) { keys.Content = RecordingHint(Keyboard.Modifiers); return; }
            if (key is Key.Back or Key.Delete) { Set(""); Finish(); keys.Opacity = 1; return; }
            if (Keyboard.Modifiers == ModifierKeys.None) { Toast("请组合 Ctrl、Alt、Shift 或 Win 使用", Ui.InfoBarSeverity.Warning); return; }
            Set(new KeyGestureConverter().ConvertToInvariantString(new KeyGesture(key, Keyboard.Modifiers))!);
            Finish(); keys.Opacity = 1;
        };

        if (nested)
        {
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(controls, Dock.Right); row.Children.Add(controls);
            var label = Text(title, 13, FontWeights.Normal, "TextFillColorPrimaryBrush", new Thickness(0)); label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            panel.Children.Add(row);
        }
        else Card(panel, icon, title, description, controls, prominent);
        return shortcut;
    }

    private static UIElement RecordingHint(ModifierKeys held = ModifierKeys.None)
    {
        var parts = Modifiers(held).ToList();
        if (parts.Count > 0) { var panel = (StackPanel)Keycaps(string.Join("+", parts) + "+…"); return panel; }
        return Text("请按下组合键…", 12.5, FontWeights.Normal, "TextFillColorSecondaryBrush", new Thickness(8, 4, 8, 4));
    }

    private static IEnumerable<string> Modifiers(ModifierKeys keys)
    {
        if (keys.HasFlag(ModifierKeys.Windows)) yield return "Win";
        if (keys.HasFlag(ModifierKeys.Control)) yield return "Ctrl";
        if (keys.HasFlag(ModifierKeys.Alt)) yield return "Alt";
        if (keys.HasFlag(ModifierKeys.Shift)) yield return "Shift";
    }

    /// <summary>Accent keycaps like PowerToys: one chip per key.</summary>
    private static UIElement Keycaps(string gesture, bool small = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (string.IsNullOrWhiteSpace(gesture)) { panel.Children.Add(Text("未设置", small ? 11.5 : 12.5, FontWeights.Normal, "TextFillColorTertiaryBrush", new Thickness(8, 4, 8, 4))); return panel; }
        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string label = Pretty(raw);
            var cap = new Border { CornerRadius = new CornerRadius(small ? 4 : 5), Padding = small ? new Thickness(7, 2, 7, 3) : new Thickness(8, 3, 8, 4), Margin = new Thickness(0, 0, small ? 4 : 5, 0), MinWidth = small ? 24 : 30 };
            if (label == "…") { cap.BorderThickness = new Thickness(1); cap.SetResourceReference(Border.BorderBrushProperty, "SystemAccentColorPrimaryBrush"); }
            else cap.SetResourceReference(Border.BackgroundProperty, "SystemAccentColorPrimaryBrush");
            var text = new TextBlock { Text = label, FontSize = small ? 11 : 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            if (label == "…") text.SetResourceReference(TextBlock.ForegroundProperty, "SystemAccentColorPrimaryBrush");
            else text.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
            if (label == "Win") { text.Text = "\uE782"; text.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); text.FontWeight = FontWeights.Normal; text.Margin = new Thickness(0, 2, 0, 0); }
            cap.Child = text; panel.Children.Add(cap);
        }
        return panel;
    }

    private static string Pretty(string key) => key switch
    {
        "Control" or "Ctrl" => "Ctrl", "Windows" or "Win" => "Win", "Return" => "Enter", "Escape" => "Esc", "Next" => "PgDn", "Prior" => "PgUp",
        "OemComma" => ",", "OemPeriod" => ".", "OemQuestion" => "/", "Oem1" or "OemSemicolon" => ";", "OemPlus" => "=", "OemMinus" => "-",
        "Oem3" or "OemTilde" => "`", "Oem4" or "OemOpenBrackets" => "[", "Oem6" or "OemCloseBrackets" => "]", "Oem5" or "OemPipe" => "\\", "Oem7" or "OemQuotes" => "'",
        _ when key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]) => key[1..],
        _ when key.StartsWith("NumPad", StringComparison.Ordinal) => "Num " + key[6..],
        _ => key,
    };

    // ================= hero art =================
    private static LinearGradientBrush Gradient(params string[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (int i = 0; i < stops.Length; i++) brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), stops.Length == 1 ? 0 : i / (double)(stops.Length - 1)));
        brush.Freeze(); return brush;
    }

    private static UIElement Bubbles()
    {
        var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        foreach (var (x, y, r, a) in new[] { (0.72, -0.35, 220.0, 0x22), (0.9, 0.55, 160.0, 0x1A), (0.05, 0.75, 120.0, 0x14), (0.45, -0.2, 90.0, 0x12) })
        {
            var circle = new Ellipse { Width = r, Height = r, Fill = new SolidColorBrush(Color.FromArgb((byte)a, 0xFF, 0xFF, 0xFF)) };
            canvas.Loaded += (_, _) => { Canvas.SetLeft(circle, x * canvas.ActualWidth - r / 2); Canvas.SetTop(circle, y * canvas.ActualHeight); };
            canvas.SizeChanged += (_, _) => { Canvas.SetLeft(circle, x * canvas.ActualWidth - r / 2); Canvas.SetTop(circle, y * canvas.ActualHeight); };
            canvas.Children.Add(circle);
        }
        return canvas;
    }

    private static readonly SolidColorBrush MockSurface = Frozen(Color.FromArgb(0xEE, 0x26, 0x27, 0x2D));
    private static readonly SolidColorBrush MockText = Frozen(Color.FromRgb(0xEE, 0xEE, 0xF2));
    private static readonly SolidColorBrush MockDim = Frozen(Color.FromRgb(0x9A, 0x9C, 0xA6));
    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static Border MockWindow(UIElement content, double width) => new()
    {
        Width = width, CornerRadius = new CornerRadius(10, 10, 0, 0), Background = MockSurface, Padding = new Thickness(14, 12, 14, 8), Child = content,
        BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1, 1, 1, 0),
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = .35 },
    };

    private static UIElement MockRow(string glyph, string text, string? color = null, bool selected = false)
    {
        var row = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 1, 0, 1), Background = selected ? Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent };
        var dock = new DockPanel(); row.Child = dock;
        UIElement lead = color != null
            ? new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), Margin = new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }
            : new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 11, Foreground = MockDim, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        dock.Children.Add(lead);
        dock.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = MockText, TextTrimming = TextTrimming.CharacterEllipsis });
        return row;
    }

    private static UIElement RackMock()
    {
        var canvas = new Canvas { Width = 300, Height = 214, HorizontalAlignment = HorizontalAlignment.Right };
        string[] texts = { "会议纪要已发送", "D:\\Projects\\AirTools", "git commit -m \"ui\"", "下午 3 点评审", "192.168.30.13" };
        string[] colors = { "#FF7A6B", "#FFB454", "#F5D04C", "#6FCF8E", "#5AB0F0" };
        for (int i = 0; i < texts.Length; i++)
        {
            double t = i / (double)(texts.Length), y = 150 - i * 34, x = 250 - 46 * Math.Sin(Math.PI * Math.Min(1, (i + .5) / texts.Length));
            bool hover = i == 2;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(9), Background = MockSurface, Width = hover ? 176 : 138, Height = hover ? 30 : 25, Padding = new Thickness(10, 0, 8, 0),
                BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                RenderTransformOrigin = new Point(1, .5), RenderTransform = new RotateTransform(hover ? 0 : -6 * Math.Cos(Math.PI * t)),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Direction = 270, Opacity = .3 },
            };
            var dock = new DockPanel { VerticalAlignment = VerticalAlignment.Center }; pill.Child = dock;
            dock.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            dock.Children.Add(new TextBlock { Text = texts[i], FontSize = hover ? 11.5 : 10.5, Foreground = MockText, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            Canvas.SetLeft(pill, x - pill.Width); Canvas.SetTop(pill, y - pill.Height / 2);
            Panel.SetZIndex(pill, hover ? 10 : 1);
            canvas.Children.Add(pill);
        }
        var button = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = MockSurface, BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1), Child = new TextBlock { Text = "\uE77F", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14, Foreground = MockText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        Canvas.SetLeft(button, 262); Canvas.SetTop(button, 176); canvas.Children.Add(button);
        return canvas;
    }

    private static UIElement HistoryMock()
    {
        var stack = new StackPanel();
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 8), Child = new TextBlock { Text = "\uE721   搜索剪切板内容…", FontFamily = new FontFamily("Segoe Fluent Icons, Microsoft YaHei UI"), FontSize = 10.5, Foreground = MockDim } });
        stack.Children.Add(MockRow("\uE718", "置顶 · 常用回复模板"));
        stack.Children.Add(MockRow("\uE8C8", "https://github.com/…", selected: true));
        stack.Children.Add(MockRow("\uE91B", "截图 1280 × 720"));
        stack.Children.Add(MockRow("\uE8B7", "report-final.pdf"));
        return MockWindow(stack, 300);
    }

    private static UIElement SearchMock()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "\uE721   wire", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe UI"), FontSize = 13, Foreground = MockText, Margin = new Thickness(2, 0, 0, 10) });
        stack.Children.Add(MockRow("\uE768", "Wireshark 远程抓包", selected: true));
        stack.Children.Add(MockRow("\uE756", "Python VPN 监控"));
        stack.Children.Add(MockRow("\uE81C", "剪切板管理器"));
        stack.Children.Add(MockRow("\uE8B7", "设置"));
        return MockWindow(stack, 310);
    }

    private static UIElement PaletteMock()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "   AirTools", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe UI"), FontSize = 13, Foreground = MockText, Margin = new Thickness(2, 0, 0, 10) });
        stack.Children.Add(MockRow("", "AirTools 快速命令", selected: true));
        stack.Children.Add(MockRow("", "AirTools 剪切板"));
        stack.Children.Add(MockRow("", "设置"));
        return MockWindow(stack, 290);
    }

    private static UIElement MonitorMock()
    {
        var bar = new Border { CornerRadius = new CornerRadius(8), Background = MockSurface, Padding = new Thickness(12, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 70), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = .35 } };
        var row = new StackPanel { Orientation = Orientation.Horizontal }; bar.Child = row;
        foreach (var (label, value, color) in new[] { ("CPU", "23%", "#A6E3A1"), ("RAM", "48%", "#89B4FA"), ("Disk", "1.2M", "#F9E2AF"), ("↓", "3.4M", "#F9E2AF") })
        {
            row.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = MockDim, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        return bar;
    }

    private static void Animate(IAnimatable target, DependencyProperty property, double to, int ms)
        => target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    private static string[] Lines(string text) => text.Replace("\r", "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
