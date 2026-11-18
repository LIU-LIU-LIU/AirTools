using AirTools.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

internal sealed partial class ClipboardPage : DynamicListPage, IDisposable
{
    private IListItem[] _items = [];
    private readonly LatestQuery _query = new();
    private long _lastRefresh;
    private string _category = "all";
    public ClipboardPage()
    {
        Id = "airtools.clipboard"; Title = "AirTools 剪切板"; Name = "打开"; PlaceholderText = "搜索剪切板历史…"; ShowDetails = true;
        Icon = new IconInfo("\uE77F");
        var filters = new ClipboardFilters(); Filters = filters;
        filters.PropChanged += (_, args) => { if (args.PropertyName == "CurrentFilterId") { _category = filters.CurrentFilterId; Refresh(); } };
        EmptyContent = new CommandItem(new ActionCommand("刷新", Refresh)) { Title = "暂无历史", Subtitle = "后台开始记录后，复制内容即可出现" };
        Refresh();
    }
    public override IListItem[] GetItems() { if (Environment.TickCount64 - _lastRefresh > 500) Refresh(); return _items; }
    public override void UpdateSearchText(string oldSearch, string newSearch) => Refresh(newSearch);
    private void Refresh() => Refresh(SearchText);
    private async void Refresh(string query)
    {
        var token = _query.Begin(); _lastRefresh = Environment.TickCount64;
        try
        {
            await Task.Delay(80, token);
            var reply = await AgentClient.CallAsync(new("clipboard.list", Query: query, Data: _category), token);
            var entries = AgentClient.Read<ClipboardEntry[]>(reply);
            var items = entries.Select(Entry)
                .Append(new ListItem(new ActionCommand("刷新", Refresh)) { Title = entries.Length == 0 ? "暂无历史 · 刷新" : "刷新历史及运行状态", Subtitle = reply.Message }).ToArray();
            _query.TryPublish(token, () => { _items = items; RaiseItemsChanged(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _query.TryPublish(token, () => { _items = [new ListItem(new ActionCommand("重试", Refresh)) { Title = "无法读取剪切板", Subtitle = ex.Message }]; RaiseItemsChanged(); }); }
    }
    private IListItem Entry(ClipboardEntry entry)
    {
        var title = entry.Kind == "Image" ? "图片" : entry.Text.Replace('\n', ' ').Replace('\r', ' ');
        if (title.Length > 140) title = title[..140] + "…";
        var openLink = _category == "Link" && entry.Kind == "Link";
        return new ListItem(new RemoteCommand(openLink ? "用默认应用打开连接" : "粘贴到原窗口", new(openLink ? "clipboard.open" : "clipboard.paste", entry.Id), dismiss: true))
        {
            Title = title,
            Subtitle = $"{(entry.Pinned ? "📌 · " : "")}{(entry.Kind == "Link" ? "连接" : entry.Kind)} · {entry.Time:g}",
            Icon = new IconInfo(entry.Kind == "Image" ? "\uEB9F" : "\uE8A5"),
            Details = new Details { Title = entry.Kind, Body = entry.Kind == "Image" ? "上下文菜单 → 完整图片预览（缩放 / 拖动）" : entry.Text,
                HeroImage = entry.Thumbnail.Length > 0 ? ImageIcon(entry.Thumbnail) : new IconInfo("") },
            MoreCommands = [
                .. entry.Kind == "Link" ? new ICommandContextItem[] { new CommandContextItem(new RemoteCommand("用默认应用打开连接", new("clipboard.open", entry.Id), dismiss: true)) } : [],
                .. openLink ? new ICommandContextItem[] { new CommandContextItem(new RemoteCommand("粘贴到原窗口", new("clipboard.paste", entry.Id), dismiss: true)) } : [],
                new CommandContextItem(new RemoteCommand("仅复制", new("clipboard.copy", entry.Id), dismiss: true)) { RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: Windows.System.VirtualKey.Enter) },
                new CommandContextItem(new RemoteCommand(entry.Pinned ? "取消置顶" : "置顶", new("clipboard.pin", entry.Id), Refresh)),
                new CommandContextItem(new RemoteCommand("删除", new("clipboard.delete", entry.Id), Refresh)),
                .. entry.Kind == "Image" ? new ICommandContextItem[] { new CommandContextItem(new RemoteCommand("完整图片预览", new("clipboard.preview", entry.Id), dismiss: true)) { RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: Windows.System.VirtualKey.P) } } : [],
                new CommandContextItem(new ConfirmRemoteCommand("清空非置顶历史", new("clipboard.clear"), Refresh)),
                new CommandContextItem(new ActionCommand("刷新历史", Refresh)),
            ],
        };
    }

    private static IconInfo ImageIcon(string base64)
    {
        var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        { writer.WriteBytes(Convert.FromBase64String(base64)); writer.StoreAsync().AsTask().GetAwaiter().GetResult(); writer.DetachStream(); }
        var reference = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream);
        return new IconInfo(new IconData(reference));
    }
    public void Dispose() => _query.Dispose();
}

internal sealed partial class ClipboardFilters : Filters
{
    public ClipboardFilters() { CurrentFilterId = "all"; }
    public override IFilterItem[] GetFilters() => [
        new Filter { Id = "all", Name = "全部" }, new Filter { Id = "pinned", Name = "置顶" },
        new Filter { Id = "Text", Name = "文本" }, new Filter { Id = "Link", Name = "连接" }, new Filter { Id = "Image", Name = "图片" }, new Filter { Id = "Files", Name = "文件" },
    ];
}
