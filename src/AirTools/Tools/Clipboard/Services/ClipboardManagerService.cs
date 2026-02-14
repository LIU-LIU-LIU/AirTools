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
using AirTools.Tools.Clipboard.Models;

namespace AirTools.Tools.Clipboard.Services
{
    public class ClipboardManagerService
    {
        private readonly object _lock = new();
        private bool _isSelfCopying;
        private readonly string _dataFilePath;

        public int MaxHistoryCount { get; set; } = 500;
        public ObservableCollection<ClipboardItem> Items { get; } = new();
        public event EventHandler<ClipboardItem>? ItemAdded;

        public ClipboardManagerService()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AirTools", "Clipboard");
            Directory.CreateDirectory(appData);
            _dataFilePath = Path.Combine(appData, "history.json");
            LoadHistory();
        }

        public void HandleClipboardChange()
        {
            if (_isSelfCopying) return;

            try
            {
                ClipboardItem? item = null;

                if (System.Windows.Clipboard.ContainsImage())
                {
                    var image = System.Windows.Clipboard.GetImage();
                    if (image == null) return;
                    if (image.CanFreeze) image.Freeze();

                    item = new ClipboardItem
                    {
                        ItemType = ClipboardItemType.Image,
                        ImageThumbnail = image,
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
            catch { }
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

        public void CopyToClipboard(ClipboardItem item)
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
                        if (item.ImageThumbnail != null)
                            System.Windows.Clipboard.SetImage(item.ImageThumbnail);
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
            }
            catch { }
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

        private void SaveHistory()
        {
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
                File.WriteAllText(_dataFilePath, JsonSerializer.Serialize(saveable, new JsonSerializerOptions { WriteIndented = false }));
            }
            catch { }
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
            catch { }
        }

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
