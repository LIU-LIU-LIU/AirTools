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
using System.Runtime.InteropServices;
using AirTools.Tools.Clipboard.Models;

namespace AirTools.Tools.Clipboard.Services
{
    public class ClipboardManagerService : IDisposable
    {
        private readonly object _lock = new();
        private uint _selfSequence;
        private uint _lastSequence;
        private string? _pendingSelfText;
        private DateTime _pendingSelfTextAt;
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        private readonly NativeTextClipboard _nativeTextClipboard = new();
        private readonly string _dataFilePath;
        private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private Task _pendingSave = Task.CompletedTask;
        private readonly LinkedList<(string Id, BitmapSource Image)> _decoded = new();
        public long ImageBudget { get; set; } = 128L * 1024 * 1024;
        public string? LastError { get; private set; }
        private bool _historyUnreadable;

        public int MaxHistoryCount { get; set; } = 500;
        /// <summary>Unpinned items older than this many days are dropped; 0 keeps everything.</summary>
        public int RetentionDays { get; set; }
        public bool RecordImages { get; set; } = true;
        public bool RecordFiles { get; set; } = true;
        /// <summary>While paused nothing new is recorded (existing history stays).</summary>
        public bool Paused { get; set; }

        /// <summary>Applies user limits and trims existing history to them.</summary>
        public void Configure(int maxItems, int retentionDays, bool recordImages, bool recordFiles, bool paused, int imageBudgetMb)
        {
            MaxHistoryCount = Math.Max(1, maxItems); RetentionDays = Math.Max(0, retentionDays);
            RecordImages = recordImages; RecordFiles = recordFiles; Paused = paused;
            ImageBudget = Math.Max(1, imageBudgetMb) * 1024L * 1024;
            if (Enforce()) SaveHistory();
        }

        /// <summary>Drops expired and surplus unpinned items. Returns true if anything was removed.</summary>
        public bool Enforce()
        {
            bool changed = false;
            lock (_lock)
            {
                if (RetentionDays > 0)
                {
                    var cutoff = DateTime.Now.AddDays(-RetentionDays);
                    foreach (var old in Items.Where(x => !x.IsPinned && x.CopyTime < cutoff).ToList()) { Items.Remove(old); changed = true; }
                }
                while (Items.Count > MaxHistoryCount)
                {
                    var last = Items.LastOrDefault(x => !x.IsPinned);
                    if (last == null) break;
                    Items.Remove(last); changed = true;
                }
                while (Items.Sum(x => x.ImageData?.LongLength ?? 0) > ImageBudget)
                {
                    var oldest = Items.LastOrDefault(x => !x.IsPinned && x.ItemType == ClipboardItemType.Image);
                    if (oldest == null) break;
                    Items.Remove(oldest); changed = true;
                }
            }
            return changed;
        }
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
            uint sequence = GetClipboardSequenceNumber();
            if (sequence != 0 && (sequence == _selfSequence || sequence == _lastSequence)) { if (sequence == _selfSequence) _pendingSelfText = null; return; }
            LastError = null;
            if (Paused) { _lastSequence = sequence; return; }

            try
            {
                ClipboardItem? item = null;

                if (RecordImages && System.Windows.Clipboard.ContainsImage())
                {
                    var image = System.Windows.Clipboard.GetImage();
                    if (image == null) return;
                    if (image.CanFreeze) image.Freeze();

                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using var memory = new MemoryStream(); encoder.Save(memory); var bytes = memory.ToArray();
                    if (bytes.LongLength > ImageBudget) { LastError = $"图片超过 {ImageBudget / 1024 / 1024} MB 内存预算，未加入历史（可在设置 → 剪切板历史中调高）"; return; }
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
                    // WM_CLIPBOARDUPDATE can be queued before SetClipboardData
                    // returns. Consume our own write by content as a race-safe
                    // second guard; it prevents a rack redraw loop.
                    if (_pendingSelfText != null && DateTime.UtcNow - _pendingSelfTextAt < TimeSpan.FromSeconds(2) && text == _pendingSelfText)
                    {
                        _pendingSelfText = null;
                        _lastSequence = sequence;
                        return;
                    }

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
                            _lastSequence = sequence;
                            ItemAdded?.Invoke(this, existing);
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
                else if (RecordFiles && System.Windows.Clipboard.ContainsFileDropList())
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
                    _lastSequence = sequence;
                    lock (_lock) InsertAfterPinned(item);
                    Enforce();
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
                switch (item.ItemType)
                {
                    case ClipboardItemType.Text:
                        _pendingSelfText = item.Text;
                        _pendingSelfTextAt = DateTime.UtcNow;
                        _nativeTextClipboard.Write(item.Text);
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
                _selfSequence = GetClipboardSequenceNumber();
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
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

        /// <summary>Explicit, repeat-safe import. Never overwrites either source or an unreadable destination.</summary>
        public int ImportHistory(string source)
        {
            if (_historyUnreadable) throw new IOException("当前历史无法读取，请先备份修复，导入未执行");
            var json = File.ReadAllText(source);
            var imported = JsonSerializer.Deserialize<List<SerializableItem>>(json) ?? throw new IOException("历史文件为空");
            var backup = Path.Combine(Path.GetDirectoryName(_dataFilePath)!, "Imports"); Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"), json);
            int count = 0;
            foreach (var item in imported.OrderByDescending(x => x.CopyTime))
            {
                if (item.ItemType != ClipboardItemType.Text || string.IsNullOrWhiteSpace(item.Text)) continue;
                var hash = ComputeHash(item.Text);
                if (Items.Any(x => x.ItemType == ClipboardItemType.Text && x.Text == item.Text)) continue;
                Items.Add(new ClipboardItem { ItemType = ClipboardItemType.Text, Text = item.Text, ContentHash = hash, IsPinned = item.IsPinned, CopyTime = item.CopyTime });
                count++;
            }
            var ordered = Items.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.CopyTime).ToArray();
            for (int i = 0; i < ordered.Length; i++) Items.Move(Items.IndexOf(ordered[i]), i);
            Enforce(); // after sorting, so the oldest unpinned items are the ones trimmed
            SaveHistory(); return count;
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

        public void Dispose() { _saveTimer.Stop(); QueueSave(); _pendingSave.GetAwaiter().GetResult(); _decoded.Clear(); _nativeTextClipboard.Dispose(); }

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
