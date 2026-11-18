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
    public partial class ClipboardWindow : Wpf.Ui.Controls.FluentWindow
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
            IsVisibleChanged += (_, _) => { if (IsVisible) _refreshTimer.Start(); else _refreshTimer.Stop(); };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _appSettings = ClipboardAppSettings.Load();
            ApplyThemeFromParent();
            RefreshList();
            _refreshTimer.Start();
            UpdateStatus("正在记录剪切板");
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
            // Colours now come from the WPF-UI theme dictionaries, which follow the app theme automatically.
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

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

        private async void LstClipboard_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstClipboard.SelectedItem is ClipboardItem item)
            {
                if (item.ItemType == ClipboardItemType.Image && item.ImageThumbnail != null)
                {
                    var image = _clipManager.GetImage(item);
                    if (image != null) new ImageViewerWindow(image) { Owner = this }.ShowDialog();
                    return;
                }
                if (_previousForegroundHwnd != IntPtr.Zero)
                {
                    var error = await PasteHelper.PasteToTargetAsync(this, _previousForegroundHwnd, () => _clipManager.CopyToClipboardAsync(item));
                    if (error != null) MessageBox.Show(error, "AirTools");
                }
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

        private void BtnShowAll_Click(object sender, RoutedEventArgs e) { _currentFilter = FilterMode.All; RefreshList(); }
        private void BtnShowPinned_Click(object sender, RoutedEventArgs e) { _currentFilter = FilterMode.Pinned; RefreshList(); }
        private void BtnShowText_Click(object sender, RoutedEventArgs e) { _currentFilter = FilterMode.Text; RefreshList(); }
        private void BtnShowImage_Click(object sender, RoutedEventArgs e) { _currentFilter = FilterMode.Image; RefreshList(); }
        private void BtnShowFiles_Click(object sender, RoutedEventArgs e) { _currentFilter = FilterMode.Files; RefreshList(); }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaxRestore_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;


        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app) { app.ShowLauncherSettings("剪切板历史"); return; }
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
            var selected = LstClipboard.SelectedItem;
            if (LstClipboard.ItemsSource is not System.Collections.ObjectModel.ObservableCollection<ClipboardItem> visible)
            { visible = new(); LstClipboard.ItemsSource = visible; }
            for (int i = visible.Count - 1; i >= 0; i--) if (!list.Contains(visible[i])) visible.RemoveAt(i);
            for (int i = 0; i < list.Count; i++) { int old = visible.IndexOf(list[i]); if (old < 0) visible.Insert(i, list[i]); else if (old != i) visible.Move(old, i); }
            if (selected != null && visible.Contains((ClipboardItem)selected)) LstClipboard.SelectedItem = selected;
            else if (visible.Count > 0) LstClipboard.SelectedIndex = 0;
            EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LstClipboard.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtCount.Text = $"共 {_clipManager.Items.Count} 条 · 置顶 {_clipManager.Items.Count(x => x.IsPinned)} 条";
            bool filtered = keyword.Length > 0 || _currentFilter != FilterMode.All;
            EmptyTitle.Text = filtered ? "没有匹配的记录" : "还没有剪切板历史";
            EmptyBody.Text = filtered ? "换个关键字，或切换到“全部”" : "复制文本、图片或文件后会出现在这里";
            BtnShowAll.IsChecked = _currentFilter == FilterMode.All;
            BtnShowPinned.IsChecked = _currentFilter == FilterMode.Pinned;
            BtnShowText.IsChecked = _currentFilter == FilterMode.Text;
            BtnShowImage.IsChecked = _currentFilter == FilterMode.Image;
            BtnShowFiles.IsChecked = _currentFilter == FilterMode.Files;
        }

        private void UpdateStatus(string text)
        {
            TxtStatus.Text = text;
            // Brief pulse on the status dot so actions feel acknowledged.
            var pulse = new System.Windows.Media.Animation.DoubleAnimation(.2, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new System.Windows.Media.Animation.CubicEase() };
            StatusDot.BeginAnimation(OpacityProperty, pulse);
        }

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
