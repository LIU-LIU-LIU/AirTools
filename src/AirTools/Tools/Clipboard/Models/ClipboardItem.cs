using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AirTools.Tools.Clipboard.Models
{
    public class ClipboardItem : INotifyPropertyChanged
    {
        private bool _isPinned;
        private string _text = string.Empty;

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public ClipboardItemType ItemType { get; set; }

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        public BitmapSource? ImageThumbnail { get; set; }
        public string[]? FilePaths { get; set; }
        public DateTime CopyTime { get; set; } = DateTime.Now;

        public bool IsPinned
        {
            get => _isPinned;
            set { _isPinned = value; OnPropertyChanged(); OnPropertyChanged(nameof(PinIcon)); }
        }

        public string DisplayText =>
            ItemType switch
            {
                ClipboardItemType.Text => Text.Length > 500 ? Text[..500] + "..." : Text,
                ClipboardItemType.Image => "[图片]",
                ClipboardItemType.Files => $"[文件] {string.Join(", ", FilePaths ?? Array.Empty<string>())}",
                _ => string.Empty
            };

        public string PreviewText
        {
            get
            {
                var text = DisplayText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                return text.Length > 120 ? text[..120] + "..." : text;
            }
        }

        public string TimeDisplay
        {
            get
            {
                var span = DateTime.Now - CopyTime;
                if (span.TotalSeconds < 60) return "刚刚";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
                if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
                return CopyTime.ToString("MM-dd HH:mm");
            }
        }

        public string PinIcon => IsPinned ? "📌" : "📍";

        public string SizeDisplay =>
            ItemType switch
            {
                ClipboardItemType.Text => $"{Text.Length} 字符",
                ClipboardItemType.Image => "图片",
                ClipboardItemType.Files => $"{FilePaths?.Length ?? 0} 个文件",
                _ => ""
            };

        public string ContentHash { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public enum ClipboardItemType
    {
        Text,
        Image,
        Files
    }
}
