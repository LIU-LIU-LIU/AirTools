using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirTools.Services;

namespace AirTools;

public sealed class LauncherSettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppHost _host;
    private readonly LauncherSettings _draft;
    private readonly StackPanel _form = new() { Margin = new Thickness(22) };
    private readonly ListBox _tasks = new() { MinWidth = 190, Margin = new Thickness(12) };
    private Action? _commitFields;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
    public LauncherSettingsWindow(AppHost host)
    {
        _host = host; _draft = host.Settings.Clone();
        Title = "AirTools · 设置与快速启动"; Width = 1000; Height = 780; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel(); Content = root;
        var titleBar = new Wpf.Ui.Controls.TitleBar { Title = "Air Tools · 设置与快速启动", ShowMaximize = false };
        DockPanel.SetDock(titleBar, Dock.Top); root.Children.Add(titleBar);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(Button("保存并应用", Save)); bottom.Children.Add(Button("关闭", () => Close()));
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var tabs = new TabControl { Margin = new Thickness(12) }; root.Children.Add(tabs);
        var general = new StackPanel { Margin = new Thickness(22) };
        tabs.Items.Add(new TabItem { Header = "通用与快捷键", Content = new ScrollViewer { Content = general, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var launch = Field(general, "搜索框快捷键", _draft.Hotkey); AddRecorder(general, launch);
        var clip = Field(general, "剪切板快捷键", _draft.ClipboardHotkey); AddRecorder(general, clip);
        var c = Field(general, "剪切板模式键（单个英文字母）", _draft.ClipboardModeKey);
        var f = Field(general, "文件模式键（单个英文字母）", _draft.FileModeKey);
        var dirs = Field(general, "文件搜索目录（每行一个）", string.Join(Environment.NewLine, _draft.Directories), true);
        var favorites = Field(general, "收藏文件路径（每行一个）", string.Join(Environment.NewLine, _draft.Favorites), true);
        var theme = new ComboBox { ItemsSource = new[] { "跟随系统", "浅色", "深色" }, SelectedIndex = AppThemeSettings.Load().ThemeMode, Margin = new Thickness(0, 6, 0, 12) };
        general.Children.Add(new TextBlock { Text = "主题" }); general.Children.Add(theme);
        var startup = new CheckBox { Content = "开机启动（托盘常驻）", IsChecked = StartupService.IsEnabled(), Margin = new Thickness(0, 8, 0, 16) }; general.Children.Add(startup);
        Action generalCommit = () =>
        {
            _draft.Hotkey = launch.Text.Trim(); _draft.ClipboardHotkey = clip.Text.Trim();
            _draft.ClipboardModeKey = c.Text.Trim().ToLowerInvariant(); _draft.FileModeKey = f.Text.Trim().ToLowerInvariant();
            if (_draft.ClipboardModeKey.Length != 1 || _draft.FileModeKey.Length != 1 || _draft.ClipboardModeKey == _draft.FileModeKey || !char.IsAsciiLetter(_draft.ClipboardModeKey[0]) || !char.IsAsciiLetter(_draft.FileModeKey[0])) throw new ArgumentException("模式键必须为不同的单个英文字母");
            _draft.Directories = Lines(dirs.Text).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _draft.Favorites = Lines(favorites.Text).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        };
        _saveGeneral = generalCommit;
        _saveTheme = () => { var settings = AppThemeSettings.Load(); settings.ThemeMode = theme.SelectedIndex; settings.Save(); StartupService.SetEnabled(startup.IsChecked == true); };
        var taskGrid = new Grid(); taskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); taskGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel(); Grid.SetColumn(left, 0); taskGrid.Children.Add(left);
        var actions = new WrapPanel { Margin = new Thickness(8) }; DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions);
        actions.Children.Add(Button("新增", () => { CommitTask(); var task = new LaunchTask(); _draft.Tasks.Add(task); Reload(task); }));
        actions.Children.Add(Button("复制", () => { CommitTask(); if (_tasks.SelectedItem is LaunchTask task) { var copy = JsonSerializer.Deserialize<LaunchTask>(JsonSerializer.Serialize(task))!; copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " 副本"; copy.Hotkey = ""; _draft.Tasks.Add(copy); Reload(copy); } }));
        actions.Children.Add(Button("删除", () => { if (_tasks.SelectedItem is LaunchTask task && MessageBox.Show("删除任务「" + task.Name + "」？", "删除任务", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { _commitFields = null; _draft.Tasks.Remove(task); Reload(_draft.Tasks.FirstOrDefault()); } }));
        left.Children.Add(_tasks);
        var scroll = new ScrollViewer { Content = _form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); taskGrid.Children.Add(scroll);
        tabs.Items.Add(new TabItem { Header = "快速启动任务", Content = taskGrid });
        _tasks.SelectionChanged += (_, e) => { if (e.RemovedItems.Count > 0) { try { CommitTask(); } catch (Exception ex) { _status.Text = ex.Message; } } if (_tasks.SelectedItem is LaunchTask task) Edit(task); else _form.Children.Clear(); };
        Reload(_draft.Tasks.FirstOrDefault());
    }
    private readonly Action _saveGeneral;
    private readonly Action _saveTheme;
    private void Save()
    {
        try { CommitTask(); _saveGeneral(); _host.ApplySettings(_draft); _saveTheme(); _host.RefreshTheme(); _status.Text = "已保存，快捷键立即生效"; }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void CommitTask() => _commitFields?.Invoke();
    private void Reload(LaunchTask? selected) { _tasks.ItemsSource = null; _tasks.ItemsSource = _draft.Tasks; _tasks.SelectedItem = selected; }
    private void Edit(LaunchTask task)
    {
        _form.Children.Clear();
        var name = Field(_form, "任务名称", task.Name);
        var alias = Field(_form, "搜索别名", task.Alias);
        var icon = Field(_form, "图标（字符）", task.Icon);
        _form.Children.Add(new TextBlock { Text = "任务类型", Margin = new Thickness(0, 8, 0, 4) });
        var kind = new ComboBox { ItemsSource = Enum.GetValues<LaunchKind>(), SelectedItem = task.Kind }; _form.Children.Add(kind);
        var hotkey = Field(_form, "独立快捷键（可留空）", task.Hotkey); AddRecorder(_form, hotkey);
        var exe = Field(_form, "程序路径 / Wireshark 路径", task.Executable);
        var dir = Field(_form, "工作目录", task.WorkingDirectory);
        var args = Field(_form, "程序参数（每行一个，整行作为一个参数，不需要额外引号）", string.Join(Environment.NewLine, task.Arguments), true);
        var script = Field(_form, "PowerShell 脚本（仅 PowerShell 类型使用）", task.Script, true);
        _form.Children.Add(new TextBlock { Text = "终端关闭策略", Margin = new Thickness(0, 8, 0, 4) });
        var policy = new ComboBox { ItemsSource = Enum.GetValues<TerminalPolicy>(), SelectedItem = task.TerminalPolicy }; _form.Children.Add(policy);
        _form.Children.Add(new TextBlock { Text = "Close：结束关闭 · KeepOnFailure：失败保留 · Keep：始终保留", FontSize = 11, Margin = new Thickness(0, 4, 0, 12) });
        var sshPanel = new StackPanel(); _form.Children.Add(sshPanel);
        sshPanel.Children.Add(new Separator()); sshPanel.Children.Add(new TextBlock { Text = "SSH 远程抓包", FontWeight = FontWeights.SemiBold, FontSize = 18 });
        var host = Field(sshPanel, "主机", task.Host); var port = Field(sshPanel, "端口", task.Port.ToString());
        var user = Field(sshPanel, "用户名", task.Username);
        sshPanel.Children.Add(new TextBlock { Text = "密码 / 私钥口令（留空保留原值）", Margin = new Thickness(0, 8, 0, 4) }); var password = new PasswordBox(); sshPanel.Children.Add(password);
        var clearPassword = new CheckBox { Content = "清除已保存密码", Margin = new Thickness(0, 6, 0, 4) }; sshPanel.Children.Add(clearPassword);
        var key = Field(sshPanel, "私钥文件（使用密码认证时留空）", task.PrivateKey);
        var iface = Field(sshPanel, "抓包接口", task.Interface); var tcpdump = Field(sshPanel, "远端 tcpdump 路径", task.Tcpdump);
        var filter = Field(sshPanel, "抓包过滤器（默认排除 SSH，可清空）", task.CaptureFilter);
        var extra = Field(sshPanel, "远端额外参数（Shell 参数，仅填写可信内容）", task.ExtraCaptureArguments);
        var fingerprint = Field(sshPanel, "已信任主机指纹（换服务器时可清空）", task.Fingerprint);
        void Visibility() { sshPanel.Visibility = (LaunchKind)kind.SelectedItem == LaunchKind.SshCapture ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; }
        kind.SelectionChanged += (_, _) => Visibility(); Visibility();
        _commitFields = () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("任务名称不能为空");
            task.Name = name.Text.Trim(); task.Alias = alias.Text; task.Icon = icon.Text; task.Kind = (LaunchKind)kind.SelectedItem;
            task.Hotkey = hotkey.Text.Trim(); task.Executable = exe.Text.Trim(); task.WorkingDirectory = dir.Text.Trim();
            task.Arguments = args.Text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries); task.Script = script.Text; task.TerminalPolicy = (TerminalPolicy)policy.SelectedItem;
            if (!int.TryParse(port.Text, out var parsed) || parsed is < 1 or > 65535) throw new ArgumentException("SSH 端口应为 1–65535");
            if (task.Host != host.Text.Trim() || task.Port != parsed) task.Fingerprint = ""; else task.Fingerprint = fingerprint.Text.Trim();
            task.Host = host.Text.Trim(); task.Port = parsed; task.Username = user.Text.Trim(); task.PrivateKey = key.Text.Trim(); task.Interface = iface.Text.Trim(); task.Tcpdump = tcpdump.Text.Trim(); task.CaptureFilter = filter.Text.Trim(); task.ExtraCaptureArguments = extra.Text;
            if (clearPassword.IsChecked == true) task.EncryptedPassword = "";
            if (password.Password.Length > 0) task.EncryptedPassword = TaskRunner.Protect(password.Password);
            password.Clear(); clearPassword.IsChecked = false;
        };
        sshPanel.Children.Add(Button("测试 SSH 与 tcpdump", async () => { try { CommitTask(); _status.Text = "正在连接…"; await _host.Runner.TestAsync(task); fingerprint.Text = task.Fingerprint; _status.Text = "SSH 与 tcpdump 检查通过；抓包权限将在启动时检查"; } catch (Exception ex) { _status.Text = ex.Message; } }));
        var commands = new WrapPanel(); _form.Children.Add(commands);
        commands.Children.Add(Button("保存任务并运行", () => { try { CommitTask(); _saveGeneral(); _host.ApplySettings(_draft); _ = _host.Runner.RunAsync(_host.Settings.Tasks.First(x => x.Id == task.Id)); } catch (Exception ex) { _status.Text = ex.Message; } }));
        commands.Children.Add(Button("停止抓包", () => _host.Runner.Stop(task.Id)));
        commands.Children.Add(Button("查看上次日志", () => { var log = TaskRunner.LogPath(task.Id); var exit = TaskRunner.ResultPath(task.Id); MessageBox.Show((File.Exists(exit) ? "退出码：" + File.ReadAllText(exit) + "\n\n" : "") + (File.Exists(log) ? File.ReadAllText(log) : "暂无运行日志"), task.Name); }));
    }
    private static string[] Lines(string text) => text.Replace("\r", "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static TextBox Field(Panel panel, string label, string value, bool multiline = false)
    {
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 5) });
        var box = new TextBox { Text = value, Padding = new Thickness(7), AcceptsReturn = multiline, MinHeight = multiline ? 65 : 30, MaxHeight = multiline ? 140 : 40, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
        panel.Children.Add(box); return box;
    }
    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "AirTools"); } }; return button;
    }
    private static void AddRecorder(Panel panel, TextBox target)
    {
        var button = new Button { Content = "录制快捷键", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) }; panel.Children.Add(button);
        button.Click += (_, _) => { button.Content = "请按组合键，Esc 取消"; button.Tag = true; button.Focus(); };
        button.PreviewKeyDown += (_, e) =>
        {
            if (button.Tag is not true) return;
            e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { button.Tag = false; button.Content = "录制快捷键"; return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            if (Keyboard.Modifiers == ModifierKeys.None) return;
            target.Text = new KeyGestureConverter().ConvertToInvariantString(new KeyGesture(key, Keyboard.Modifiers))!;
            button.Tag = false; button.Content = "录制快捷键";
        };
    }
}
