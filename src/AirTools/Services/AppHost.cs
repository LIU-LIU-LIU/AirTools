using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Threading.Tasks;
using AirTools.Tools.Clipboard.Models;
using AirTools.Tools.Clipboard;
using AirTools.Tools.Clipboard.Services;
using AirTools.Tools.SystemMonitor;

namespace AirTools.Services;

public sealed class AppHost : IDisposable
{
    public LauncherSettings Settings { get; private set; } = LauncherSettings.Load();
    public ClipboardManagerService Clipboard { get; } = new();
    public TaskRunner Runner { get; } = new();
    private readonly GlobalHotkeys _hotkeys = new();
    private readonly ClipboardMonitor _monitor = new();
    private readonly Window _messageWindow = new() { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon _icon;
    private readonly LauncherWindow _launcher;
    public RecentTextRack Rack { get; } = new();
    private readonly RackWindow _rackWindow;
    private bool _pasting;
    private ClipboardWindow? _clipboardWindow;
    private MonitorWindow? _monitorWindow;
    private LauncherSettingsWindow? _settingsWindow;
    private bool _disposed;
    private PipeServer? _pipe;
    public string HotkeyWarning { get; private set; } = "";
    public AppHost()
    {
        _launcher = new(this);
        Rack.Capacity = Settings.RackCapacity;
        _rackWindow = new(Rack, PasteRackAsync, () => ShowSettings("晾衣架"), () => ShowClipboard(PasteHelper.GetPreviousForegroundWindow()), Settings.RackCollapsed)
        { SlotHint = index => index < Settings.RackSlotHotkeys.Length ? Settings.RackSlotHotkeys[index] : "" };
        _rackWindow.CollapsedChanged += collapsed =>
        {
            Settings.RackCollapsed = collapsed;
            try { Settings.Save(); } catch (Exception ex) { Notify("设置保存失败：" + ex.Message); }
        };
        _rackWindow.SetAnchor(Settings.RackAnchorX, Settings.RackAnchorY);
        _rackWindow.AnchorChanged += (x, y) =>
        {
            Settings.RackAnchorX = x; Settings.RackAnchorY = y;
            try { Settings.Save(); } catch (Exception ex) { Notify("位置保存失败：" + ex.Message); }
        };
        _rackWindow.HideRequested += ToggleRack;
        _rackWindow.Notice += Notify;
        Rack.LoadPinned(Settings.RackPinned);
        Rack.PinsChanged += () =>
        {
            Settings.RackPinned = Rack.PinnedTexts.ToList();
            try { Settings.Save(); } catch (Exception ex) { Notify("固定项保存失败：" + ex.Message); }
        };
        Clipboard.ItemAdded += ClipboardItemAdded;
        ApplyClipboardLimits();
        _monitor.Start(_messageWindow); new System.Windows.Interop.WindowInteropHelper(_messageWindow).EnsureHandle();
        _monitor.ClipboardChanged += ClipboardChanged;
        using (var resource = Application.GetResourceStream(new Uri("pack://application:,,,/AirTools;component/Assets/AirTools.ico")).Stream) _icon = new System.Drawing.Icon(resource);
        _tray = new System.Windows.Forms.NotifyIcon { Icon = _icon, Text = "AirTools · " + Settings.Hotkey + " 搜索", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip { Renderer = new TrayMenuRenderer(), Font = new System.Drawing.Font("Microsoft YaHei UI", 9f), ShowImageMargin = false, Padding = new System.Windows.Forms.Padding(4) };
        menu.Items.Add("搜索 " + Settings.Hotkey, null, (_, _) => _launcher.Toggle());
        menu.Items.Add("剪切板历史", null, (_, _) => ShowClipboard(IntPtr.Zero));
        menu.Items.Add("显示 / 隐藏晾衣架", null, (_, _) => ToggleRack());
        menu.Items.Add("显示 / 隐藏资源监控", null, (_, _) => ToggleMonitor());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => ShowSettings());
        menu.Items.Add("退出 AirTools", null, (_, _) => Application.Current.Shutdown());
        foreach (System.Windows.Forms.ToolStripItem item in menu.Items) if (item is not System.Windows.Forms.ToolStripSeparator) item.Padding = new System.Windows.Forms.Padding(10, 6, 24, 6);
        menu.Opening += (_, _) => ((TrayMenuRenderer)menu.Renderer).Dark = IsDarkTheme;
        _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => _launcher.Toggle();
        Runner.StatusChanged += Notify; Runner.SaveCredentials = () => Settings.Save();
        RefreshTheme();
        try { Register(Settings); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); Notify("快捷键注册失败，请从托盘打开设置：" + ex.Message); }
        if (HotkeyWarning.Length > 0) Notify(HotkeyWarning);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += ThemeChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        if (Settings.RackEnabled) _rackWindow.ShowDocked();
        if (Settings.MonitorEnabled) ShowMonitor();
        _pipe = new PipeServer(this); // lets the Command Palette extension search history and run tasks
    }
    private void DisplayChanged(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(_rackWindow.Dock);
    public bool IsDarkTheme { get { var mode = AppThemeSettings.Load().ThemeMode; return mode == 2 || mode == 0 && ThemeService.IsSystemDarkMode(); } }
    private void ClipboardItemAdded(object? sender, ClipboardItem item) { if (item.ItemType == ClipboardItemType.Text) Rack.Add(item.Text); }
    public void ToggleRack()
    {
        Settings.RackEnabled = !_rackWindow.IsVisible;
        if (Settings.RackEnabled) _rackWindow.ShowDocked(); else _rackWindow.Hide();
        try { Settings.Save(); } catch (Exception ex) { Notify("设置保存失败：" + ex.Message); }
    }
    public async Task<string?> PasteRackAsync(RackEntry entry)
    {
        if (_pasting) return "正在粘贴，请稍候";
        var target = PasteHelper.GetPreviousForegroundWindow();
        _pasting = true;
        try
        {
            var error = await PasteHelper.PasteWithoutActivationAsync(target,
                () => Clipboard.CopyToClipboardAsync(new ClipboardItem { ItemType = ClipboardItemType.Text, Text = entry.Text }), Settings.PasteCompatibilityFallback);
            if (error != null) Notify(error);
            return error;
        }
        catch (Exception ex) { var error = "粘贴失败：" + ex.Message; Notify(error); return error; }
        finally { _pasting = false; }
    }
    private async void PasteSlot(int index)
    {
        if (index >= Rack.Items.Count) { Notify("这个衣架位置还没有内容"); return; }
        await PasteRackAsync(Rack.Items[index]);
    }
    private void ThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        RefreshTheme();
        // Taskbar moves / auto-hide change the work area: keep the rack glued to the corner.
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.Desktop or Microsoft.Win32.UserPreferenceCategory.General) _rackWindow.Dock();
    });
    public void ShowSearch() => _launcher.Toggle();
    public void RefreshTheme()
    {
        _launcher.ApplyTheme(); bool dark = IsDarkTheme;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light, Wpf.Ui.Controls.WindowBackdropType.Mica);
        ThemeService.ApplyTheme(_messageWindow.Resources, dark);
        _rackWindow.ApplyTheme(dark);
        _clipboardWindow?.ApplyThemeFromParent(); _monitorWindow?.ApplyThemeFromParent();
    }
    private void ClipboardChanged(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(() => { Clipboard.HandleClipboardChange(); if (Clipboard.LastError != null) Notify(Clipboard.LastError); if (_clipboardWindow?.IsVisible == true) _clipboardWindow.RefreshList(); });
    private void Register(LauncherSettings settings)
    {
        settings.Validate();
        var bindings = new List<(string, Action)> { (settings.Hotkey, _launcher.Toggle), (settings.ClipboardHotkey, () => ShowClipboard(PasteHelper.GetPreviousForegroundWindow())) };
        bindings.Add((settings.RackHotkey, ToggleRack));
        for (int i = 0; i < Math.Min(5, settings.RackSlotHotkeys.Length); i++) { int slot = i; bindings.Add((settings.RackSlotHotkeys[i], () => PasteSlot(slot))); }
        bindings.AddRange(settings.Tasks.Where(x => x.Hotkey.Length > 0).Select(task => (task.Hotkey, (Action)(() => { _ = Runner.RunAsync(task); }))));
        var names = new List<(string Name, string Gesture)> { ("搜索", settings.Hotkey), ("剪切板历史", settings.ClipboardHotkey), ("显示 / 隐藏晾衣架", settings.RackHotkey) };
        names.AddRange(settings.RackSlotHotkeys.Select((key, index) => ($"粘贴第 {index + 1} 件", key)));
        names.AddRange(settings.Tasks.Select(task => (task.Name, task.Hotkey)));
        var conflicts = _hotkeys.Apply(bindings, allowConflicts: true);
        HotkeyWarning = string.Join("；", conflicts.Select(error =>
            names.FirstOrDefault(x => x.Gesture == error.Gesture).Name + "：" + error.Gesture + " 被占用，暂未启用"));
    }
    public void ApplySettings(LauncherSettings settings)
    {
        var next = settings.Clone();
        // The settings window edits a stale copy; values the app owns at runtime must survive a save from it.
        next.RackCollapsed = Settings.RackCollapsed; next.RackAnchorX = Settings.RackAnchorX; next.RackAnchorY = Settings.RackAnchorY;
        next.MonitorEnabled = Settings.MonitorEnabled; next.RecentFiles = Settings.RecentFiles; next.RackPinned = Settings.RackPinned;
        Register(next);
        try { next.Save(); } catch { try { Register(Settings); } catch { } throw; }
        Settings = next;
        ApplyClipboardLimits();
        Rack.Capacity = Settings.RackCapacity;
        if (Settings.RackEnabled) _rackWindow.ShowDocked(); else _rackWindow.Hide();
        _rackWindow.RefreshHints();
        _tray.Text = "AirTools · " + Settings.Hotkey + " 搜索";
        _tray.ContextMenuStrip!.Items[0].Text = "搜索 " + Settings.Hotkey;
    }
    public void ShowClipboard(IntPtr target)
    {
        if (_clipboardWindow == null) { _clipboardWindow = new(Clipboard) { Owner = _messageWindow }; _clipboardWindow.Closed += (_, _) => _clipboardWindow = null; }
        _clipboardWindow.SetPreviousForegroundWindow(target); _clipboardWindow.ShowAndActivate();
    }
    private void ApplyClipboardLimits() => Clipboard.Configure(Settings.ClipboardMaxItems, Settings.ClipboardRetentionDays, Settings.ClipboardRecordImages, Settings.ClipboardRecordFiles, Settings.ClipboardPaused, Settings.ClipboardImageBudgetMb);
    public void ShowMonitor()
    {
        if (_monitorWindow == null)
        {
            _monitorWindow = new() { Owner = _messageWindow };
            _monitorWindow.SettingsRequested += () => ShowSettings("系统监控");
            _monitorWindow.Closed += (_, _) =>
            {
                _monitorWindow = null;
                if (_disposed) return; // app shutdown keeps "was open" so it comes back next launch
                Settings.MonitorEnabled = false;
                try { Settings.Save(); } catch { }
            };
        }
        _monitorWindow.ApplyThemeFromParent(); _monitorWindow.Show();
        if (!Settings.MonitorEnabled) { Settings.MonitorEnabled = true; try { Settings.Save(); } catch { } }
    }
    public void ToggleMonitor() { if (_monitorWindow != null) _monitorWindow.Close(); else ShowMonitor(); }
    public void ApplyMonitorSettings(AirTools.Tools.SystemMonitor.Models.MonitorSettings settings) { settings.Save(); _monitorWindow?.ApplySettings(settings); }
    public void ResetMonitorPosition()
    {
        var latest = AirTools.Tools.SystemMonitor.Models.MonitorSettings.Load(); latest.Left = null; latest.Top = null;
        ApplyMonitorSettings(latest);
    }
    public void ResetRackPosition() => _rackWindow.ResetPosition();
    public void ShowSettings() => ShowSettings(null, WindowState.Normal);
    /// <summary>Opens settings on a given page (e.g. "晾衣架", "剪切板历史", "系统监控").</summary>
    public void ShowSettings(string page) { ShowSettings(null, WindowState.Normal); _settingsWindow?.Navigate(page); }
    /// <summary>Opens settings; a bounds hint reopens a fresh window in place (used by "discard changes").</summary>
    public void ShowSettings(Rect? bounds, WindowState state)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new(this); _settingsWindow.Closed += (sender, _) => { if (ReferenceEquals(sender, _settingsWindow)) _settingsWindow = null; };
            if (bounds is Rect r) { _settingsWindow.WindowStartupLocation = WindowStartupLocation.Manual; _settingsWindow.Left = r.Left; _settingsWindow.Top = r.Top; _settingsWindow.Width = r.Width; _settingsWindow.Height = r.Height; _settingsWindow.WindowState = state; }
        }
        _settingsWindow.Show(); _settingsWindow.Activate();
    }
    public void SuspendHotkeys() => _hotkeys.Apply(Array.Empty<(string, Action)>());
    public void ResumeHotkeys() { if (_disposed) return; try { Register(Settings); } catch (Exception ex) { Notify(ex.Message); } }
    public void Notify(string message)
    {
        if (!Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.BeginInvoke(() => Notify(message)); return; }
        if (_disposed) return;
        _tray.ShowBalloonTip(4000, "AirTools", message, System.Windows.Forms.ToolTipIcon.Info);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= ThemeChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        Clipboard.ItemAdded -= ClipboardItemAdded;
        _hotkeys.Dispose(); _monitor.Dispose(); Runner.Dispose(); Clipboard.Dispose();
        _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); _icon.Dispose();
        _pipe?.Dispose();
        _launcher.DisposeWindow(); _rackWindow.Close(); _clipboardWindow?.Close(); _monitorWindow?.Close(); _settingsWindow?.CloseSilently(); _messageWindow.Close();
    }
}
