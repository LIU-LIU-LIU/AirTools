using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AirTools.Tools.Clipboard;
using AirTools.Tools.Clipboard.Services;
using AirTools.Tools.SystemMonitor;

namespace AirTools.Services;

public sealed class AppHost : IDisposable
{
    public LauncherSettings Settings { get; private set; } = LauncherSettings.Load();
    public ClipboardManagerService Clipboard { get; } = new();
    public FileSearchService Files { get; } = new();
    public TaskRunner Runner { get; } = new();
    private readonly GlobalHotkeys _hotkeys = new();
    private readonly ClipboardMonitor _monitor = new();
    private readonly Window _messageWindow = new() { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon _icon;
    private readonly LauncherWindow _launcher;
    private ClipboardWindow? _clipboardWindow;
    private MonitorWindow? _monitorWindow;
    private LauncherSettingsWindow? _settingsWindow;
    private bool _disposed;
    public AppHost()
    {
        _launcher = new(this);
        _monitor.Start(_messageWindow); new System.Windows.Interop.WindowInteropHelper(_messageWindow).EnsureHandle();
        _monitor.ClipboardChanged += ClipboardChanged;
        using (var resource = Application.GetResourceStream(new Uri("pack://application:,,,/AirTools;component/Assets/AirTools.ico")).Stream) _icon = new System.Drawing.Icon(resource);
        _tray = new System.Windows.Forms.NotifyIcon { Icon = _icon, Text = "AirTools · " + Settings.Hotkey + " 搜索", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("搜索 " + Settings.Hotkey, null, (_, _) => _launcher.Toggle());
        menu.Items.Add("剪切板管理器", null, (_, _) => ShowClipboard(IntPtr.Zero));
        menu.Items.Add("设置与快速启动", null, (_, _) => ShowSettings());
        menu.Items.Add("系统资源监控", null, (_, _) => ShowMonitor());
        menu.Items.Add("退出", null, (_, _) => Application.Current.Shutdown());
        _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => _launcher.Toggle();
        Runner.StatusChanged += Notify; Runner.SaveCredentials = () => Settings.Save();
        RefreshTheme();
        try { Register(Settings); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); Notify("快捷键注册失败，请从托盘打开设置：" + ex.Message); }
        Files.Rebuild(Settings.Directories);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += ThemeChanged;
    }
    private void ThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) => Application.Current.Dispatcher.BeginInvoke(RefreshTheme);
    public void ShowSearch() => _launcher.Toggle();
    public void RefreshTheme()
    {
        _launcher.ApplyTheme(); var theme = AppThemeSettings.Load();
        ThemeService.ApplyTheme(_messageWindow.Resources, theme.ThemeMode == 2 || theme.ThemeMode == 0 && ThemeService.IsSystemDarkMode());
        _clipboardWindow?.ApplyThemeFromParent(); _monitorWindow?.ApplyThemeFromParent();
    }
    private void ClipboardChanged(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(() => { Clipboard.HandleClipboardChange(); if (Clipboard.LastError != null) Notify(Clipboard.LastError); if (_clipboardWindow?.IsVisible == true) _clipboardWindow.RefreshList(); });
    private void Register(LauncherSettings settings)
    {
        var bindings = new List<(string, Action)> { (settings.Hotkey, _launcher.Toggle), (settings.ClipboardHotkey, () => ShowClipboard(PasteHelper.GetPreviousForegroundWindow())) };
        bindings.AddRange(settings.Tasks.Where(x => x.Hotkey.Length > 0).Select(task => (task.Hotkey, (Action)(() => { _ = Runner.RunAsync(task); }))));
        _hotkeys.Apply(bindings);
    }
    public void ApplySettings(LauncherSettings settings)
    {
        var next = settings.Clone();
        Register(next);
        try { next.Save(); } catch { Register(Settings); throw; }
        bool directoriesChanged = !Settings.Directories.SequenceEqual(next.Directories, StringComparer.OrdinalIgnoreCase);
        Settings = next;
        if (directoriesChanged) Files.Rebuild(Settings.Directories);
        _tray.Text = "AirTools · " + Settings.Hotkey + " 搜索";
        _tray.ContextMenuStrip!.Items[0].Text = "搜索 " + Settings.Hotkey;
    }
    public void ShowClipboard(IntPtr target)
    {
        if (_clipboardWindow == null) { _clipboardWindow = new(Clipboard) { Owner = _messageWindow }; _clipboardWindow.Closed += (_, _) => _clipboardWindow = null; }
        _clipboardWindow.SetPreviousForegroundWindow(target); _clipboardWindow.ShowAndActivate();
    }
    public void ShowMonitor()
    {
        if (_monitorWindow == null) { _monitorWindow = new() { Owner = _messageWindow }; _monitorWindow.Closed += (_, _) => _monitorWindow = null; }
        _monitorWindow.ApplyThemeFromParent(); _monitorWindow.Show(); _monitorWindow.Activate();
    }
    public void ShowSettings()
    {
        if (_settingsWindow == null) { _settingsWindow = new(this); _settingsWindow.Closed += (_, _) => _settingsWindow = null; }
        _settingsWindow.Show(); _settingsWindow.Activate();
    }
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
        _hotkeys.Dispose(); _monitor.Dispose(); Files.Dispose(); Runner.Dispose(); Clipboard.Dispose();
        _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); _icon.Dispose();
        _launcher.DisposeWindow(); _clipboardWindow?.Close(); _monitorWindow?.Close(); _settingsWindow?.Close(); _messageWindow.Close();
    }
}
