using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using AirTools.Services;
using AirTools.Tools.Clipboard;
using AirTools.Tools.Clipboard.Models;
using AirTools.Tools.Clipboard.Services;

namespace AirTools;

public enum SearchMode { All, Clipboard, Files }
public sealed record SearchResult(string Title, string Subtitle, string Icon, object Value);
public sealed class LauncherViewModel : ObservableObject
{
    public ObservableCollection<SearchResult> Results { get; } = new();
    private SearchMode _mode;
    public SearchMode Mode { get => _mode; set => SetProperty(ref _mode, value); }
}
public interface ISearchProvider { Task<SearchResult[]> SearchAsync(string query, CancellationToken token); }

public partial class LauncherWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppHost _host;
    private readonly LauncherViewModel _viewModel = new();
    private CancellationTokenSource? _queryCancel;
    private IntPtr _target;
    private bool _composing;
    private bool _executing;
    private bool _closing;
    private DateTime _shownAt;
    public LauncherWindow(AppHost host)
    {
        _host = host; InitializeComponent(); DataContext = _viewModel;
        TextCompositionManager.AddPreviewTextInputStartHandler(Query, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputHandler(Query, (_, _) => { _composing = false; _ = UpdateAsync(); });
        TextCompositionManager.AddPreviewTextInputUpdateHandler(Query, (_, _) => _composing = true);
        Query.PreviewTextInput += (_, e) =>
        {
            if (_viewModel.Mode != SearchMode.All || Query.Text.Length != 0 || _composing) return;
            if (e.Text.Equals(_host.Settings.ClipboardModeKey, StringComparison.OrdinalIgnoreCase)) { SetMode(SearchMode.Clipboard); e.Handled = true; }
            else if (e.Text.Equals(_host.Settings.FileModeKey, StringComparison.OrdinalIgnoreCase)) { SetMode(SearchMode.Files); e.Handled = true; }
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) _queryCancel?.Cancel(); };
        _host.Clipboard.ItemAdded += Clipboard_ItemAdded;
    }
    private void Clipboard_ItemAdded(object? sender, ClipboardItem e) { if (IsVisible && _viewModel.Mode == SearchMode.Clipboard) _ = UpdateAsync(); }
    public void Toggle()
    {
        if (IsVisible) { Dismiss(); return; }
        _target = PasteHelper.GetPreviousForegroundWindow();
        SetMode(SearchMode.All); Query.Clear();
        ApplyTheme();
        var screen = System.Windows.Forms.Screen.FromHandle(_target).WorkingArea;
        new WindowInteropHelper(this).EnsureHandle();
        // Get the destination monitor DPI rather than assuming the previous screen scale.
        NativeMethods.PositionLauncher(this, _target, screen);
        _shownAt = DateTime.UtcNow;
        Show(); Activate(); Query.Focus();
        Dispatcher.BeginInvoke(() => { Activate(); Query.Focus(); Keyboard.Focus(Query); }, System.Windows.Threading.DispatcherPriority.Input);
        _ = UpdateAsync();
    }
    public void ApplyTheme()
    {
        var theme = AppThemeSettings.Load();
        bool dark = theme.ThemeMode == 2 || theme.ThemeMode == 0 && ThemeService.IsSystemDarkMode();
        ThemeService.ApplyTheme(Resources, dark);
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
        Resources["LauncherSurface"] = new SolidColorBrush(dark ? Color.FromArgb(190, 29, 33, 43) : Color.FromArgb(180, 247, 249, 252));
        Resources["SearchSurface"] = new SolidColorBrush(dark ? Color.FromArgb(65, 255, 255, 255) : Color.FromArgb(90, 255, 255, 255));
        Resources["SelectionSurface"] = new SolidColorBrush(dark ? Color.FromArgb(35, 169, 193, 231) : Color.FromArgb(95, 222, 231, 243));
        Resources["SoftBorder"] = new SolidColorBrush(dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(25, 80, 100, 130));
        UpdatePlaceholder();
    }
    private void Dismiss() { Hide(); PasteHelper.RestoreForeground(_target); }
    private void SetMode(SearchMode mode)
    {
        _viewModel.Mode = mode; Query.Clear();
        ModeBadge.Visibility = mode == SearchMode.All ? Visibility.Collapsed : Visibility.Visible;
        ModeText.Text = mode == SearchMode.Clipboard ? "剪切板" : "文件";
        UpdatePlaceholder();
        Hint.Text = mode == SearchMode.Clipboard ? "Enter 粘贴 · Ctrl+Enter 仅复制 · Ctrl+P 预览 · 空词 Backspace 返回" : mode == SearchMode.Files ? "Enter 打开 · Ctrl+D 收藏/取消收藏 · 空词 Backspace 返回" : "↑↓ 选择　 Enter 打开　 Esc 收起";
        _ = UpdateAsync();
    }
    private void UpdatePlaceholder()
    {
        if (SearchPlaceholder == null) return;
        SearchPlaceholder.Text = _viewModel.Mode switch { SearchMode.Clipboard => "搜索剪切板历史…", SearchMode.Files => "搜索常用文件…", _ => $"搜索任务…   {_host.Settings.ClipboardModeKey} 剪切板  ·  {_host.Settings.FileModeKey} 文件" };
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(Query.Text) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Query_TextChanged(object sender, TextChangedEventArgs e) { UpdatePlaceholder(); if (_host != null && !_composing) _ = UpdateAsync(); }
    private async Task UpdateAsync()
    {
        _queryCancel?.Cancel(); _queryCancel?.Dispose();
        var cancel = _queryCancel = new(); var token = cancel.Token;
        string query = Query.Text.Trim(); var mode = _viewModel.Mode;
        try
        {
            await Task.Delay(80, token);
            SearchResult[] values;
            if (mode == SearchMode.Clipboard)
                values = _host.Clipboard.Search(query).Take(50).Select(x => new SearchResult(x.PreviewText, x.TimeDisplay + " · " + x.SizeDisplay, x.ItemType == ClipboardItemType.Image ? "▧" : "▤", x)).ToArray();
            else if (mode == SearchMode.Files)
                values = await Task.Run(() => _host.Files.Search(query, _host.Settings, token).Select(x => new SearchResult(Path.GetFileName(x), x, "▱", x)).ToArray(), token);
            else
            {
                values = _host.Settings.Tasks.Select(x => new SearchResult(x.Name, x.Kind == LaunchKind.SshCapture ? $"SSH · {x.Username}@{x.Host} · {x.Interface}" : x.Executable + " " + string.Join(" ", x.Arguments), x.Icon, x))
                    .Prepend(new("剪切板管理器", "浏览、搜索和管理历史记录", "▤", "clipboard"))
                    .Concat(new[] { new SearchResult("文件搜索", "常用目录、收藏与最近打开", "▱", "files"), new SearchResult("设置与快速启动", "任务、快捷键、搜索目录", "⚙", "settings"), new SearchResult("系统资源监控", "CPU、内存、磁盘和网络", "◴", "monitor") })
                    .Select(x => (Item: x, Score: SearchRanking.Score(x.Title + " " + (x.Value is LaunchTask task ? task.Alias : ""), query)))
                    .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(50).Select(x => x.Item).ToArray();
            }
            token.ThrowIfCancellationRequested();
            var selected = (Results.SelectedItem as SearchResult)?.Value;
            _viewModel.Results.Clear(); foreach (var value in values) _viewModel.Results.Add(value);
            Results.SelectedItem = values.FirstOrDefault(x => Equals(x.Value, selected)) ?? values.FirstOrDefault();
            Empty.Visibility = values.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Hint.Text = ex.Message; }
    }
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_composing || e.Key == Key.ImeProcessed) return;
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
        else if (e.Key == Key.Back && Query.Text.Length == 0 && _viewModel.Mode != SearchMode.All) { SetMode(SearchMode.All); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Down) { if (Results.Items.Count > 0) { Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.Items.Count - 1); Results.ScrollIntoView(Results.SelectedItem); } e.Handled = true; }
        else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control && Results.SelectedItem is SearchResult { Value: ClipboardItem image } && image.ItemType == ClipboardItemType.Image)
        { e.Handled = true; var bitmap = _host.Clipboard.GetImage(image); if (bitmap != null) { _executing = true; try { new ImageViewerWindow(bitmap) { Owner = this }.ShowDialog(); } finally { _executing = false; Query.Focus(); } } }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control && _viewModel.Mode == SearchMode.Files && Results.SelectedItem is SearchResult { Value: string path })
        { e.Handled = true; if (!_host.Settings.Favorites.Remove(path)) _host.Settings.Favorites.Add(path); _host.Settings.Save(); Hint.Text = "已更新收藏：" + path; }
        else if (e.Key == Key.Enter) { e.Handled = true; await ExecuteAsync(); }
    }
    private async Task ExecuteAsync()
    {
        if (_executing || Results.SelectedItem is not SearchResult result) return;
        _executing = true;
        try
        {
            if (result.Value is ClipboardItem item)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control) { if (await _host.Clipboard.CopyToClipboardAsync(item)) { Hide(); PasteHelper.RestoreForeground(_target); } }
                else { var error = await PasteHelper.PasteToTargetAsync(this, _target, () => _host.Clipboard.CopyToClipboardAsync(item)); if (error != null) _host.Notify(error); }
            }
            else if (result.Value is LaunchTask task) { Hide(); _ = _host.Runner.RunAsync(task); }
            else if (_viewModel.Mode == SearchMode.Files && result.Value is string path)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); Hide();
                _host.Settings.RecentFiles.Remove(path); _host.Settings.RecentFiles.Insert(0, path);
                if (_host.Settings.RecentFiles.Count > 100) _host.Settings.RecentFiles.RemoveRange(100, _host.Settings.RecentFiles.Count - 100);
                _host.Settings.Save();
            }
            else switch (result.Value)
            {
                case "clipboard": Hide(); _host.ShowClipboard(_target); break;
                case "files": SetMode(SearchMode.Files); Query.Focus(); break;
                case "settings": Hide(); _host.ShowSettings(); break;
                case "monitor": Hide(); _host.ShowMonitor(); break;
            }
        }
        catch (Exception ex) { _host.Notify(ex.Message); }
        finally { _executing = false; }
    }
    private async void Results_DoubleClick(object sender, MouseButtonEventArgs e) => await ExecuteAsync();
    private void Settings_Click(object sender, RoutedEventArgs e) { Hide(); _host.ShowSettings(); }
    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_executing && DateTime.UtcNow - _shownAt > TimeSpan.FromMilliseconds(350)) Hide();
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    { if (!_closing) { e.Cancel = true; Dismiss(); } base.OnClosing(e); }
    public void DisposeWindow() { _closing = true; _queryCancel?.Cancel(); _host.Clipboard.ItemAdded -= Clipboard_ItemAdded; Close(); }
}
