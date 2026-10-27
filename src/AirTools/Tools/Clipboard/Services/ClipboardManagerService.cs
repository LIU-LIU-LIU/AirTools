using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;
using AirTools.Tools.Clipboard.Models;

namespace AirTools.Tools.Clipboard.Services
{
    public class ClipboardManagerService : IDisposable
    {
        private readonly object _lock = new();
        private bool _isSelfCopying;
        private readonly string _dataFilePath;
        private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private Task _pendingSave = Task.CompletedTask;
        private readonly LinkedList<(string Id, BitmapSource Image)> _decoded = new();
        public const long ImageBudget = 128L * 1024 * 1024;
        public string? LastError { get; private set; }
        private bool _historyUnreadable;

        public int MaxHistoryCount { get; set; } = 500;
        public ObservableCollection<ClipboardItem> Items { get; } = new();
        public event EventHandler<ClipboardItem>? ItemAdded;

        public ClipboardManagerService()
        {
            var appData = Path.Combine(AirTools.Services.LauncherSettings.DataDirectory, "Clipboard");
            Directory.CreateDirectory(appData);
            _dataFilePath = Path.Combine(appData, "history.json");
            LoadHistory();
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); QueueSave(); };
        }

        public void HandleClipboardChange()
        {
            if (_isSelfCopying) return;
            LastError = null;

            try
            {
                ClipboardItem? item = null;

                if (System.Windows.Clipboard.ContainsImage())
                {
                    var image = System.Windows.Clipboard.GetImage();
                    if (image == null) return;
                    if (image.CanFreeze) image.Freeze();

                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using var memory = new MemoryStream(); encoder.Save(memory); var bytes = memory.ToArray();
                    if (bytes.LongLength > ImageBudget) { LastError = "图片超过 128 MB 内存预算，未加入历史"; return; }
                    while (Items.Sum(x => x.ImageData?.LongLength ?? 0) + bytes.LongLength > ImageBudget)
                    {
                        var oldest = Items.LastOrDefault(x => !x.IsPinned && x.ItemType == ClipboardItemType.Image);
                        if (oldest == null) { LastError = "置顶图片占满内存预算，未加入新图片"; return; }
                        Items.Remove(oldest);
                    }
                    var thumbnail = new BitmapImage();
                    using var thumbnailStream = new MemoryStream(bytes);
                    thumbnail.BeginInit(); thumbnail.StreamSource = thumbnailStream; thumbnail.CacheOption = BitmapCacheOption.OnLoad; thumbnail.DecodePixelWidth = Math.Min(280, image.PixelWidth); thumbnail.EndInit(); thumbnail.Freeze();
                    item = new ClipboardItem
                    {
                        ItemType = ClipboardItemType.Image,
                        ImageThumbnail = thumbnail,
                        ImageData = bytes,
                        Text = "[图片]",
                        ContentHash = Guid.NewGuid().ToString(),
                        CopyTime = DateTime.Now
                    };
                }
                else if (System.Windows.Clipboard.ContainsText())
                {
                    var text = System.Windows.Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(text)) return;

                    var hash = ComputeHash(text);
                    lock (_lock)
                    {
                        var existing = Items.FirstOrDefault(x => x.ContentHash == hash && !x.IsPinned);
                        if (existing != null)
                        {
                            Items.Remove(existing);
                            existing.CopyTime = DateTime.Now;
                            InsertAfterPinned(existing);
                            SaveHistory();
                            return;
                        }
                    }

                    item = new ClipboardItem
                    {
                        ItemType = ClipboardItemType.Text,
                        Text = text,
                        ContentHash = hash,
                        CopyTime = DateTime.Now
                    };
                }
                else if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var fileList = System.Windows.Clipboard.GetFileDropList();
                    var fileArray = new string[fileList.Count];
                    fileList.CopyTo(fileArray, 0);

                    item = new ClipboardItem
                    {
                        ItemType = ClipboardItemType.Files,
                        FilePaths = fileArray,
                        Text = string.Join("\n", fileArray),
                        ContentHash = ComputeHash(string.Join("\n", fileArray)),
                        CopyTime = DateTime.Now
                    };
                }

                if (item != null)
                {
                    lock (_lock)
                    {
                        InsertAfterPinned(item);
                        while (Items.Count > MaxHistoryCount)
                        {
                            var last = Items.LastOrDefault(x => !x.IsPinned);
                            if (last != null) Items.Remove(last);
                            else break;
                        }
                    }
                    ItemAdded?.Invoke(this, item);
                    SaveHistory();
                }
            }
            catch (Exception ex) { LastError = "读取剪切板失败：" + ex.Message; }
        }

        private void InsertAfterPinned(ClipboardItem item)
        {
            if (item.IsPinned)
            {
                Items.Insert(0, item);
                return;
            }
            int insertIndex = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].IsPinned)
                    insertIndex = i + 1;
                else
                    break;
            }
            Items.Insert(insertIndex, item);
        }

        public BitmapSource? GetImage(ClipboardItem item)
        {
            var node = _decoded.First;
            while (node != null) { if (node.Value.Id == item.Id) { _decoded.Remove(node); _decoded.AddFirst(node); return node.Value.Image; } node = node.Next; }
            if (item.ImageData == null) return item.ImageThumbnail;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            using var stream = new MemoryStream(item.ImageData); image.StreamSource = stream; image.EndInit(); image.Freeze();
            long size = (long)image.PixelWidth * image.PixelHeight * 4;
            if (size <= 64L * 1024 * 1024)
            {
                _decoded.AddFirst((item.Id, image));
                while (_decoded.Sum(x => (long)x.Image.PixelWidth * x.Image.PixelHeight * 4) > 64L * 1024 * 1024 || _decoded.Count > 2) _decoded.RemoveLast();
            }
            return image;
        }
        public async Task<bool> CopyToClipboardAsync(ClipboardItem item)
        {
            for (int attempt = 0; attempt < 4; attempt++) { if (TryCopy(item)) return true; await Task.Delay(30 * (attempt + 1)); }
            return false;
        }
        public void CopyToClipboard(ClipboardItem item) => TryCopy(item);
        private bool TryCopy(ClipboardItem item)
        {
            try
            {
                _isSelfCopying = true;
                switch (item.ItemType)
                {
                    case ClipboardItemType.Text:
                        System.Windows.Clipboard.SetText(item.Text);
                        break;
                    case ClipboardItemType.Image:
                        var image = GetImage(item);
                        if (image == null) return false;
                        System.Windows.Clipboard.SetImage(image);
                        break;
                    case ClipboardItemType.Files:
                        if (item.FilePaths != null)
                        {
                            var collection = new System.Collections.Specialized.StringCollection();
                            collection.AddRange(item.FilePaths);
                            System.Windows.Clipboard.SetFileDropList(collection);
                        }
                        break;
                }
                LastError = null;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    new Action(() => _isSelfCopying = false),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        public void TogglePin(ClipboardItem item)
        {
            lock (_lock)
            {
                item.IsPinned = !item.IsPinned;
                Items.Remove(item);
                if (item.IsPinned)
                    Items.Insert(0, item);
                else
                    InsertAfterPinned(item);
            }
            SaveHistory();
        }

        public void DeleteItem(ClipboardItem item)
        {
            lock (_lock) Items.Remove(item);
            SaveHistory();
        }

        public void ClearUnpinned()
        {
            lock (_lock)
            {
                var unpinned = Items.Where(x => !x.IsPinned).ToList();
                foreach (var i in unpinned) Items.Remove(i);
            }
            SaveHistory();
        }

        public IEnumerable<ClipboardItem> Search(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return Items;
            var lower = keyword.ToLowerInvariant();
            return Items.Where(x =>
                x.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (x.FilePaths != null && x.FilePaths.Any(f => f.Contains(keyword, StringComparison.OrdinalIgnoreCase))));
        }

        private static string ComputeHash(string content)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(bytes);
        }

        private void SaveHistory() { _saveTimer.Stop(); _saveTimer.Start(); }
        private void QueueSave()
        {
            if (_historyUnreadable) return;
            try
            {
                var saveable = Items
                    .Where(x => x.ItemType != ClipboardItemType.Image)
                    .Select(x => new SerializableItem
                    {
                        Id = x.Id,
                        ItemType = x.ItemType,
                        Text = x.Text,
                        FilePaths = x.FilePaths,
                        CopyTime = x.CopyTime,
                        IsPinned = x.IsPinned,
                        ContentHash = x.ContentHash
                    })
                    .ToList();
                var json = JsonSerializer.Serialize(saveable);
                var previous = _pendingSave;
                _pendingSave = Task.Run(async () =>
                {
                    await previous;
                    await _saveGate.WaitAsync();
                    try { await File.WriteAllTextAsync(_dataFilePath + ".tmp", json); File.Move(_dataFilePath + ".tmp", _dataFilePath, true); }
                    catch (Exception ex) { LastError = "历史保存失败：" + ex.Message; }
                    finally { _saveGate.Release(); }
                });
            }
            catch (Exception ex) { LastError = "历史序列化失败：" + ex.Message; }
        }

        private void LoadHistory()
        {
            try
            {
                if (!File.Exists(_dataFilePath)) return;
                var items = JsonSerializer.Deserialize<List<SerializableItem>>(File.ReadAllText(_dataFilePath));
                if (items == null) return;

                foreach (var si in items)
                {
                    if (si.ItemType == ClipboardItemType.Image) continue;
                    Items.Add(new ClipboardItem
                    {
                        Id = si.Id,
                        ItemType = si.ItemType,
                        Text = si.Text,
                        FilePaths = si.FilePaths,
                        CopyTime = si.CopyTime,
                        IsPinned = si.IsPinned,
                        ContentHash = si.ContentHash
                    });
                }
            }
            catch (Exception ex) { _historyUnreadable = true; LastError = "历史文件无法读取，已保护原文件：" + ex.Message; }
        }

        public void Dispose() { _saveTimer.Stop(); QueueSave(); _pendingSave.GetAwaiter().GetResult(); _decoded.Clear(); }

        private class SerializableItem
        {
            public string Id { get; set; } = "";
            public ClipboardItemType ItemType { get; set; }
            public string Text { get; set; } = "";
            public string[]? FilePaths { get; set; }
            public DateTime CopyTime { get; set; }
            public bool IsPinned { get; set; }
            public string ContentHash { get; set; } = "";
        }
    }
}
