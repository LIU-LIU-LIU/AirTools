using System;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirTools.Models;
using AirTools.Services;
using AirTools.Tools.Clipboard;
using AirTools.Tools.Clipboard.Models;
using AirTools.Tools.Clipboard.Services;
using AirTools.Tools.SystemMonitor;

namespace AirTools
{
    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private readonly ClipboardManagerService _clipManager = new();
        private readonly ClipboardMonitor _clipMonitor = new();
        private readonly HotkeyManager _hotkeyManager = new();
        private ClipboardWindow? _clipboardWindow;
        private MonitorWindow? _monitorWindow;
        private AppThemeSettings _themeSettings = new();
        private bool _isDarkMode = true;

        public MainWindow()
        {
            InitializeComponent();

            LoadTools();

            _clipMonitor.ClipboardChanged += OnClipboardChanged;
            _clipMonitor.Start(this);

            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;

            InitializeTrayIcon();
            Loaded += MainWindow_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var clipSettings = ClipboardAppSettings.Load();
            _hotkeyManager.Register(this, clipSettings.GetModifiers(), clipSettings.GetVirtualKey());
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _themeSettings = AppThemeSettings.Load();
            ApplyTheme();
            if (_themeSettings.ThemeMode == 0)
                SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }

        private void ApplyTheme()
        {
            _isDarkMode = _themeSettings.ThemeMode switch
            {
                0 => ThemeService.IsSystemDarkMode(),
                1 => false,
                2 => true,
                _ => ThemeService.IsSystemDarkMode()
            };
            ThemeService.ApplyTheme(Resources, _isDarkMode);
            _clipboardWindow?.ApplyThemeFromParent();
            _monitorWindow?.ApplyThemeFromParent();
        }

        private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (_themeSettings.ThemeMode != 0) return;
            var nowDark = ThemeService.IsSystemDarkMode();
            if (nowDark == _isDarkMode) return;
            _isDarkMode = nowDark;
            Dispatcher.BeginInvoke(() => ApplyTheme());
        }

        private void LoadTools()
        {
            ToolsList.ItemsSource = new[]
            {
                new ToolDefinition
                {
                    Id = "clipboard",
                    Name = "剪切板管理器",
                    Description = "历史记录、置顶、搜索、快捷键唤出",
                    Icon = "📋",
                    Launch = LaunchClipboard
                },
                new ToolDefinition
                {
                    Id = "sysmon",
                    Name = "系统资源监控",
                    Description = "CPU、内存、磁盘、网络实时监控",
                    Icon = "📊",
                    Launch = LaunchSystemMonitor
                }
            };
        }

        private void OnClipboardChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _clipManager.HandleClipboardChange();
                var win = _clipboardWindow;
                if (win != null && win.IsLoaded)
                    win.Dispatcher.BeginInvoke(new Action(win.RefreshList));
            });
        }

        private void OnHotkeyPressed(object? sender, EventArgs e)
        {
            var hwnd = PasteHelper.GetPreviousForegroundWindow();
            var win = GetOrCreateClipboardWindow();
            win.SetPreviousForegroundWindow(hwnd);
            win.ShowAndActivate();
        }

        private ClipboardWindow GetOrCreateClipboardWindow()
        {
            if (_clipboardWindow == null || !_clipboardWindow.IsLoaded)
            {
                _clipboardWindow?.Close();
                _clipboardWindow = new ClipboardWindow(_clipManager) { Owner = this };
                _clipboardWindow.Closed += (s, _) => { if (s == _clipboardWindow) _clipboardWindow = null; };
            }
            return _clipboardWindow;
        }

        private void LaunchClipboard()
        {
            var win = GetOrCreateClipboardWindow();
            win.SetPreviousForegroundWindow(PasteHelper.GetPreviousForegroundWindow());
            win.Show();
            win.Activate();
        }

        private void LaunchSystemMonitor()
        {
            if (_monitorWindow == null || !_monitorWindow.IsLoaded)
            {
                _monitorWindow?.Close();
                _monitorWindow = new MonitorWindow { Owner = this };
                _monitorWindow.Closed += (s, _) => { if (s == _monitorWindow) _monitorWindow = null; };
            }
            _monitorWindow.ApplyThemeFromParent();
            _monitorWindow.Show();
            _monitorWindow.Activate();
        }

        private void ToolCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ToolDefinition tool)
                tool.Launch();
        }

        private void InitializeTrayIcon()
        {
            var clipSettings = ClipboardAppSettings.Load();
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = $"Air Tools · {clipSettings.GetHotkeyDisplay()} 唤出剪切板",
                Visible = true
            };
            _trayIcon.Icon = CreateTrayIcon();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("打开 Air Tools", null, (s, _) => { Show(); WindowState = WindowState.Normal; Activate(); });
            menu.Items.Add("剪切板管理器", null, (s, _) => LaunchClipboard());
            menu.Items.Add("系统资源监控", null, (s, _) => LaunchSystemMonitor());
            menu.Items.Add("-");
            menu.Items.Add("设置", null, (s, _) => { Show(); Activate(); BtnSettings_Click(this, new RoutedEventArgs()); });
            menu.Items.Add("-");
            menu.Items.Add("退出", null, (s, _) =>
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _clipMonitor.Dispose();
                _hotkeyManager.Dispose();
                Application.Current.Shutdown();
            });

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        }

        private static System.Drawing.Icon CreateTrayIcon()
        {
            var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(137, 180, 250));
                g.FillRectangle(bgBrush, 2, 2, 28, 28);
                using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2);
                g.DrawRectangle(pen, 8, 8, 16, 16);
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow(_themeSettings, s =>
            {
                _themeSettings = s;
                ApplyTheme();
            }) { Owner = this };
            settings.ShowDialog();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
