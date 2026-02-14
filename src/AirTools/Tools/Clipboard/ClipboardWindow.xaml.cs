using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AirTools.Tools.Clipboard.Models;
using AirTools.Tools.Clipboard.Services;

namespace AirTools.Tools.Clipboard
{
    public partial class ClipboardWindow : Window
    {
        private readonly ClipboardManagerService _clipManager;
        private readonly DispatcherTimer _refreshTimer;
        private IntPtr _previousForegroundHwnd = IntPtr.Zero;

        private enum FilterMode { All, Pinned, Text, Image, Files }
        private FilterMode _currentFilter = FilterMode.All;

        private ClipboardAppSettings _appSettings = new();

        public ClipboardWindow(ClipboardManagerService clipManager)
        {
            InitializeComponent();
            _clipManager = clipManager;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _refreshTimer.Tick += (s, e) => RefreshList();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _appSettings = ClipboardAppSettings.Load();
            ApplyThemeFromParent();
            RefreshList();
            _refreshTimer.Start();
            UpdateStatus($"正在监控剪切板... · {_appSettings.GetHotkeyDisplay()} 唤出");
        }

        /// <summary>从主窗口同步主题（AirTools 统一管理主题）</summary>
        public void ApplyThemeFromParent()
        {
            if (Owner is Window owner && owner.Resources.Count > 0)
            {
                foreach (var key in new[] { "WindowGlass", "BgDark", "BgCard", "BgCardHover", "BgInput", "AccentColor", "AccentHover",
                    "TextPrimary", "TextSecondary", "TextDim", "DangerColor", "SuccessColor", "PinColor", "BorderColor", "CanvasBg" })
                {
                    if (owner.Resources.Contains(key))
                        Resources[key] = owner.Resources[key];
                }
                RefreshList();
            }
        }

        public void SetPreviousForegroundWindow(IntPtr hwnd) => _previousForegroundHwnd = hwnd;

        public void ShowAndActivate()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            TxtSearch.Focus();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            RefreshList();
        }

        private void LstClipboard_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstClipboard.SelectedItem is ClipboardItem item)
            {
                if (item.ItemType == ClipboardItemType.Image && item.ImageThumbnail != null)
                {
                    new ImageViewerWindow(item.ImageThumbnail) { Owner = this }.ShowDialog();
                    return;
                }
                if (_previousForegroundHwnd != IntPtr.Zero)
                    PasteHelper.PasteToTarget(this, _previousForegroundHwnd, () => _clipManager.CopyToClipboard(item));
                else
                    _clipManager.CopyToClipboard(item);
                UpdateStatus($"已粘贴: {item.PreviewText[..Math.Min(30, item.PreviewText.Length)]}...");
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ClipboardItem item)
            {
                _clipManager.CopyToClipboard(item);
                UpdateStatus("已复制到剪切板");
            }
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ClipboardItem item)
            {
                _clipManager.TogglePin(item);
                RefreshList();
                UpdateStatus(item.IsPinned ? "已置顶" : "已取消置顶");
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ClipboardItem item)
            {
                _clipManager.DeleteItem(item);
                RefreshList();
                UpdateStatus("已删除");
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定要清除所有非置顶的剪切板历史吗？\n\n置顶条目将会保留。", "清除确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _clipManager.ClearUnpinned();
                RefreshList();
                UpdateStatus("历史已清除");
            }
        }

        private void BtnShowPinned_Click(object sender, RoutedEventArgs e) { _currentFilter = _currentFilter == FilterMode.Pinned ? FilterMode.All : FilterMode.Pinned; RefreshList(); }
        private void BtnShowText_Click(object sender, RoutedEventArgs e) { _currentFilter = _currentFilter == FilterMode.Text ? FilterMode.All : FilterMode.Text; RefreshList(); }
        private void BtnShowImage_Click(object sender, RoutedEventArgs e) { _currentFilter = _currentFilter == FilterMode.Image ? FilterMode.All : FilterMode.Image; RefreshList(); }
        private void BtnShowFiles_Click(object sender, RoutedEventArgs e) { _currentFilter = _currentFilter == FilterMode.Files ? FilterMode.All : FilterMode.Files; RefreshList(); }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaxRestore_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Window_StateChanged(object sender, EventArgs e) => BtnMaxRestore.Content = WindowState == WindowState.Maximized ? "❐" : "□";

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new ClipboardSettingsWindow(_appSettings, s =>
            {
                _appSettings = s;
                UpdateStatus($"已更新 · {_appSettings.GetHotkeyDisplay()} 唤出");
            }) { Owner = this };
            settingsWin.SyncThemeFromOwner();
            settingsWin.ShowDialog();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _refreshTimer.Stop();
        }

        public void RefreshList()
        {
            var keyword = TxtSearch.Text?.Trim() ?? "";
            var items = _clipManager.Search(keyword);
            items = _currentFilter switch
            {
                FilterMode.Pinned => items.Where(x => x.IsPinned),
                FilterMode.Text => items.Where(x => x.ItemType == ClipboardItemType.Text),
                FilterMode.Image => items.Where(x => x.ItemType == ClipboardItemType.Image),
                FilterMode.Files => items.Where(x => x.ItemType == ClipboardItemType.Files),
                _ => items
            };

            var list = items.ToList();
            LstClipboard.ItemsSource = list;
            EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LstClipboard.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtCount.Text = $"共 {_clipManager.Items.Count} 条 · 置顶 {_clipManager.Items.Count(x => x.IsPinned)} 条";

            var activeBg = FindResource("AccentColor") as SolidColorBrush;
            var normalBg = FindResource("BgCard") as SolidColorBrush;
            BtnShowPinned.Background = _currentFilter == FilterMode.Pinned ? activeBg : normalBg;
            BtnShowText.Background = _currentFilter == FilterMode.Text ? activeBg : normalBg;
            BtnShowImage.Background = _currentFilter == FilterMode.Image ? activeBg : normalBg;
            BtnShowFiles.Background = _currentFilter == FilterMode.Files ? activeBg : normalBg;
        }

        private void UpdateStatus(string text) => TxtStatus.Text = $"{text} · {_appSettings.GetHotkeyDisplay()} 唤出";

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { TxtSearch.Focus(); TxtSearch.SelectAll(); e.Handled = true; }
            if (e.Key == Key.Enter && LstClipboard.SelectedItem is ClipboardItem item) { _clipManager.CopyToClipboard(item); UpdateStatus("已复制到剪切板"); e.Handled = true; }
            if (e.Key == Key.Delete && LstClipboard.SelectedItem is ClipboardItem delItem) { _clipManager.DeleteItem(delItem); RefreshList(); e.Handled = true; }
        }
    }
}
