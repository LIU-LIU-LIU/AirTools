using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AirTools.Tools.SystemMonitor.Models;
using AirTools.Tools.SystemMonitor.Services;

namespace AirTools.Tools.SystemMonitor
{
    public partial class MonitorWindow : Window
    {
        private readonly SystemMonitorService _monitor = new();
        private readonly DispatcherTimer _timer = new();
        private MonitorSettings _settings = new();

        public MonitorWindow()
        {
            InitializeComponent();
            _settings = MonitorSettings.Load();
            _monitor.UpdateIntervalMs = _settings.UpdateIntervalMs;
            _monitor.DiskDrive = _settings.DiskDrive;
            _monitor.NetworkAdapterId = _settings.NetworkAdapterId;
            _timer.Interval = TimeSpan.FromMilliseconds(_settings.UpdateIntervalMs);
            _timer.Tick += Timer_Tick;
            // Always-on-top utility: clicking or dragging it must not pull focus from the app being typed into.
            SourceInitialized += (_, _) => AirTools.Tools.Clipboard.Services.NonActivatingWindow.Apply(this);
        }

        /// <summary>Raised when the user picks "设置" in the bar's context menu; the host opens the unified settings page.</summary>
        public event Action? SettingsRequested;

        /// <summary>Applies new settings live (called after settings are saved).</summary>
        public void ApplySettings(MonitorSettings s)
        {
            _settings = s;
            _monitor.UpdateIntervalMs = s.UpdateIntervalMs;
            _monitor.DiskDrive = s.DiskDrive;
            _monitor.NetworkAdapterId = s.NetworkAdapterId;
            _timer.Interval = TimeSpan.FromMilliseconds(s.UpdateIntervalMs); // previously only read once at startup
            Opacity = s.WindowOpacity;
            ApplyVisibility();
            ApplyLayoutDensity();
            PositionWindow();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Opacity = _settings.WindowOpacity;
            MainBorder.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            ApplyVisibility();
            ApplyLayoutDensity();
            PositionWindow();
            _timer.Start();
            Timer_Tick(null!, EventArgs.Empty);
        }

        private void ApplyLayoutDensity()
        {
            var isRelaxed = _settings.LayoutDensity == "Relaxed";
            // 紧凑: 更小内边距和间距; 宽松: 更大内边距和间距
            MainBorder.Padding = isRelaxed ? new Thickness(10, 6, 10, 6) : new Thickness(6, 3, 6, 3);
            var itemGapH = isRelaxed ? 14.0 : 8.0;   // 横向项间距
            var itemGapV = isRelaxed ? 8.0 : 4.0;    // 竖向项间距

            // 竖向布局
            GridCpuV.Margin = new Thickness(0, 0, 0, itemGapV);
            GridMemV.Margin = new Thickness(0, 0, 0, itemGapV);
            GridDiskV.Margin = new Thickness(0, 0, 0, itemGapV);
            GridNetUpV.Margin = new Thickness(0, 0, 0, itemGapV);

            // 横向布局
            var mH = new Thickness(0, 0, itemGapH, 0);
            GridCpuH.Margin = mH;
            GridMemH.Margin = mH;
            GridDiskH.Margin = mH;
        }

        private void ApplyVisibility()
        {
            var isVertical = _settings.Orientation == "Vertical";
            PanelVertical.Visibility = isVertical ? Visibility.Visible : Visibility.Collapsed;
            PanelHorizontal.Visibility = !isVertical ? Visibility.Visible : Visibility.Collapsed;

            if (isVertical)
            {
                GridCpuV.Visibility = _settings.ShowCpu ? Visibility.Visible : Visibility.Collapsed;
                GridMemV.Visibility = _settings.ShowMemory ? Visibility.Visible : Visibility.Collapsed;
                GridDiskV.Visibility = _settings.ShowDisk ? Visibility.Visible : Visibility.Collapsed;
                PanelNetV.Visibility = _settings.ShowNetwork ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                GridCpuH.Visibility = _settings.ShowCpu ? Visibility.Visible : Visibility.Collapsed;
                GridMemH.Visibility = _settings.ShowMemory ? Visibility.Visible : Visibility.Collapsed;
                GridDiskH.Visibility = _settings.ShowDisk ? Visibility.Visible : Visibility.Collapsed;
                PanelNetH.Visibility = _settings.ShowNetwork ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void PositionWindow()
        {
            var workArea = SystemParameters.WorkArea;
            const int margin = 8;
            
            // Re-measure to get correct size
            UpdateLayout();
            
            var w = ActualWidth;
            var h = ActualHeight;

            if (_settings.Left is double savedLeft && _settings.Top is double savedTop && double.IsFinite(savedLeft) && double.IsFinite(savedTop))
            {
                // User-dragged position: keep it, but never let it sit (partly) off every screen.
                double minX = SystemParameters.VirtualScreenLeft, minY = SystemParameters.VirtualScreenTop;
                double maxX = minX + SystemParameters.VirtualScreenWidth - w, maxY = minY + SystemParameters.VirtualScreenHeight - h;
                Left = Math.Clamp(savedLeft, minX, Math.Max(minX, maxX)); Top = Math.Clamp(savedTop, minY, Math.Max(minY, maxY));
                return;
            }

            Left = _settings.Position switch {
                "BottomLeft" => workArea.Left + margin, 
                "TopRight" => workArea.Right - w - margin, 
                "TopLeft" => workArea.Left + margin, 
                _ => workArea.Right - w - margin 
            };
            
            Top = _settings.Position switch {
                "TopRight" or "TopLeft" => workArea.Top + margin,
                _ => workArea.Bottom - h - margin
            };
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            // Re-position if size changes (e.g. text length changes or orientation switch)
            PositionWindow();
        }

        /// <summary>从主窗口同步主题</summary>
        public void ApplyThemeFromParent()
        {
            if (Owner is Window owner && owner.Resources.Count > 0)
            {
                foreach (var key in new[] { "MonitorBg", "MonitorBorder", "MonitorCpuColor", "MonitorMemColor", "MonitorDiskColor", "MonitorNetColor",
                    "BgDark", "BgCard", "BgCardHover", "AccentColor", "TextPrimary", "TextSecondary", "TextDim", "BorderColor", "DangerColor" })
                {
                    if (owner.Resources.Contains(key))
                        Resources[key] = owner.Resources[key];
                }
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            var stats = _monitor.GetStats();
            var isVertical = _settings.Orientation == "Vertical";

            if (_settings.ShowCpu)
            {
                var txt = $"{stats.CpuUsage:F0}%";
                var color = GetUsageColor(stats.CpuUsage);
                if (isVertical) 
                { 
                    TxtCpuV.Text = txt; 
                    TxtCpuV.Foreground = color;
                    CpuLineV.Fill = color;
                    CpuLineV.Opacity = stats.CpuUsage > 50 ? 0.6 : 0;
                }
                else { TxtCpuH.Text = txt; TxtCpuH.Foreground = color; }
            }
            if (_settings.ShowMemory)
            {
                var txt = $"{stats.MemoryUsage:F0}%";
                var color = GetUsageColor(stats.MemoryUsage);
                if (isVertical) 
                { 
                    TxtMemV.Text = txt; 
                    TxtMemV.Foreground = color;
                    MemLineV.Fill = color;
                    MemLineV.Opacity = stats.MemoryUsage > 50 ? 0.6 : 0;
                }
                else { TxtMemH.Text = txt; TxtMemH.Foreground = color; }
            }
            
            if (_settings.ShowDisk)
            {
                var maxDisk = Math.Max(stats.DiskReadSpeed, stats.DiskWriteSpeed);
                var txt = FormatSpeed(maxDisk);
                if (isVertical) 
                { 
                    TxtDiskV.Text = txt; 
                    UpdateActivityLine(DiskLineV, maxDisk, TxtDiskV);
                }
                else 
                { 
                    TxtDiskH.Text = txt; 
                    UpdateActivityLine(DiskLineH, maxDisk, TxtDiskH);
                }
            }

            if (_settings.ShowNetwork)
            {
                var up = FormatSpeed(stats.Network.UploadSpeed);
                var down = FormatSpeed(stats.Network.DownloadSpeed);
                
                if (isVertical)
                {
                    TxtNetUpV.Text = up; 
                    UpdateActivityLine(NetUpLineV, stats.Network.UploadSpeed, TxtNetUpV);
                    TxtNetDownV.Text = down; 
                    UpdateActivityLine(NetDownLineV, stats.Network.DownloadSpeed, TxtNetDownV);
                }
                else
                {
                    TxtNetUpH.Text = up; 
                    UpdateActivityLine(NetUpLineH, stats.Network.UploadSpeed, TxtNetUpH);
                    TxtNetDownH.Text = down; 
                    UpdateActivityLine(NetDownLineH, stats.Network.DownloadSpeed, TxtNetDownH);
                }
            }
        }

        private bool IsDarkTheme()
        {
            // 通过背景颜色判断当前主题
            if (FindResource("MonitorBg") is SolidColorBrush bg)
            {
                var c = bg.Color;
                // 计算亮度 (0-255)
                double brightness = (c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
                return brightness < 128; // 亮度低于128认为是深色主题
            }
            return true; // 默认深色
        }

        private SolidColorBrush GetUsageColor(double usage)
        {
            if (!_settings.UseColorGradient)
            {
                // 使用默认主题色
                if (FindResource("MonitorCpuColor") is SolidColorBrush defaultBrush)
                    return defaultBrush;
            }

            // 根据主题和负载返回不同颜色
            bool isDark = IsDarkTheme();
            
            if (usage < 50) // 低负载 - 绿色
                return isDark ? 
                    new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)) : // 深色主题用浅绿
                    new SolidColorBrush(Color.FromRgb(0x0D, 0x72, 0x33));   // 浅色主题用深绿
            
            if (usage < 80) // 中负载 - 黄色
                return isDark ? 
                    new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)) : // 深色主题用浅黄
                    new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));   // 浅色主题用深棕
            
            // 高负载 - 红色
            return isDark ? 
                new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)) : // 深色主题用浅红
                new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));   // 浅色主题用深红
        }

        private void UpdateActivityLine(System.Windows.Shapes.Rectangle line, double speed, TextBlock textBlock)
        {
            if (!_settings.UseColorGradient)
            {
                // 使用默认主题色
                if (FindResource("MonitorDiskColor") is SolidColorBrush defaultBrush)
                {
                    line.Fill = defaultBrush;
                    textBlock.Foreground = defaultBrush;
                }
            }
            else
            {
                // 根据主题和速度返回不同颜色
                bool isDark = IsDarkTheme();
                SolidColorBrush color;
                
                if (speed > 10 * 1024 * 1024) // > 10MB/s - 红色
                    color = isDark ? 
                        new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)) : // 深色主题用浅红
                        new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));   // 浅色主题用深红
                else if (speed > 1024 * 1024) // > 1MB/s - 黄色
                    color = isDark ? 
                        new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)) : // 深色主题用浅黄
                        new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));   // 浅色主题用深棕
                else // < 1MB/s - 绿色
                    color = isDark ? 
                        new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)) : // 深色主题用浅绿
                        new SolidColorBrush(Color.FromRgb(0x0D, 0x72, 0x33));   // 浅色主题用深绿

                line.Fill = color;
                textBlock.Foreground = color;
            }

            // Blink logic
            EnsureBlinkAnimation(line, speed);
        }

        private void EnsureBlinkAnimation(System.Windows.Shapes.Rectangle line, double speed)
        {
            var storyboard = line.Tag as System.Windows.Media.Animation.Storyboard;
            if (speed <= 1024) // Idle (< 1KB/s)
            {
                if (storyboard != null)
                {
                    storyboard.Stop();
                    line.Tag = null;
                }
                line.Opacity = 0.3;
                return;
            }

            if (storyboard == null)
            {
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.3,
                    AutoReverse = true,
                    Duration = new Duration(TimeSpan.FromMilliseconds(500)),
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                storyboard = new System.Windows.Media.Animation.Storyboard();
                storyboard.Children.Add(animation);
                System.Windows.Media.Animation.Storyboard.SetTarget(animation, line);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(animation, new PropertyPath("Opacity"));
                line.Tag = storyboard;
                storyboard.Begin();
            }

            // Adjust speed ratio based on data rate
            double ratio = 1.0;
            if (speed > 10 * 1024 * 1024) ratio = 4.0; // Very fast blink
            else if (speed > 1024 * 1024) ratio = 2.0; // Fast blink
            
            storyboard.SetSpeedRatio(ratio);
        }

        private static string FormatSpeed(double bytesPerSec)
        {
            var units = new[] { "B", "K", "M", "G" };
            var i = 0;
            while (bytesPerSec >= 1024 && i < 3) { bytesPerSec /= 1024; i++; }
            return $"{bytesPerSec:F1}{units[i]}";
        }

        // ---- drag with magnetic snapping (hold Shift or Ctrl to move freely) ----
        private bool _dragging;
        private Point _dragMouse; private double _dragLeft, _dragTop;

        private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true; _dragMouse = PointToScreen(e.GetPosition(this)); _dragLeft = Left; _dragTop = Top;
            MainBorder.CaptureMouse(); e.Handled = true;
        }

        private void Bar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
            var p = PointToScreen(e.GetPosition(this)); var dpi = VisualTreeHelper.GetDpi(this);
            double left = _dragLeft + (p.X - _dragMouse.X) / dpi.DpiScaleX, top = _dragTop + (p.Y - _dragMouse.Y) / dpi.DpiScaleY;
            bool free = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
            // The visible bar sits inside a 10 px transparent margin; snap the visible edge, not the window edge.
            const double inset = 10;
            var area = WorkAreaAt(p, dpi);
            var snap = SnapService.Snap(left + inset, top + inset, ActualWidth - 2 * inset, ActualHeight - 2 * inset, area, enabled: !free);
            Left = snap.Left - inset; Top = snap.Top - inset;
        }

        private void Bar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (_dragging) { MainBorder.ReleaseMouseCapture(); e.Handled = true; } }

        private void Bar_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (Math.Abs(Left - _dragLeft) < 1 && Math.Abs(Top - _dragTop) < 1) return;
            // Remember where the user put it. Reload first so unrelated edits made in settings meanwhile are not lost.
            var latest = MonitorSettings.Load(); latest.Left = Left; latest.Top = Top; latest.Save();
            _settings.Left = Left; _settings.Top = Top;
        }

        private static Rect WorkAreaAt(Point screenPx, DpiScale dpi)
        {
            var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)screenPx.X, (int)screenPx.Y)).WorkingArea;
            return new Rect(wa.Left / dpi.DpiScaleX, wa.Top / dpi.DpiScaleY, wa.Width / dpi.DpiScaleX, wa.Height / dpi.DpiScaleY);
        }

        private void MenuItem_Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
        private void MenuItem_ResetPosition_Click(object sender, RoutedEventArgs e)
        {
            var latest = MonitorSettings.Load(); latest.Left = null; latest.Top = null; latest.Save();
            _settings.Left = null; _settings.Top = null; PositionWindow();
        }
        private void MenuItem_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer.Stop();
            _monitor.Dispose();
        }

    }
}
