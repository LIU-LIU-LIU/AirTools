using AirTools.Core;
using AirTools.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

internal sealed partial class TasksPage : DynamicListPage, IDisposable
{
    private IListItem[] _items = [];
    private readonly LatestQuery _query = new();
    private long _lastRefresh;
    public TasksPage()
    {
        Id = "airtools.tasks"; Title = "AirTools 快速任务"; Name = "打开"; PlaceholderText = "搜索任务名称或别名…";
        Icon = new IconInfo("\uE945"); Refresh();
    }
    public override IListItem[] GetItems() { if (Environment.TickCount64 - _lastRefresh > 500) Refresh(); return _items; }
    public override void UpdateSearchText(string oldSearch, string newSearch) => Refresh(newSearch);
    public void Refresh() => Refresh(SearchText);
    private async void Refresh(string query)
    {
        var token = _query.Begin(); _lastRefresh = Environment.TickCount64;
        try
        {
            await Task.Delay(80, token);
            var reply = await AgentClient.CallAsync(new("tasks.list"), token);
            var tasks = AgentClient.Read<List<LaunchTask>>(reply);
            if (token.IsCancellationRequested) return;
            var items = tasks.Select(x => (Task: x, Score: SearchRanking.Score(x.Name + " " + x.Alias, query))).Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(50).Select(x => Entry(x.Task))
                .Append(new ListItem(new ActionCommand("刷新", Refresh)) { Title = "刷新任务及运行状态", Subtitle = reply.Message }).ToArray();
            _query.TryPublish(token, () => { _items = items; RaiseItemsChanged(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _query.TryPublish(token, () => { _items = [new ListItem(new ActionCommand("重试", Refresh)) { Title = "无法读取任务", Subtitle = ex.Message }]; RaiseItemsChanged(); }); }
    }
    private static ListItem Entry(LaunchTask task) => new ListItem(new RemoteCommand("运行", new("tasks.run", task.Id), dismiss: true))
    {
        Title = task.Name, Subtitle = $"{task.Kind} · {task.Alias}", Icon = new IconInfo("\uE945"),
        MoreCommands = [
            new CommandContextItem(new RemoteCommand("测试 SSH 连接", new("tasks.test", task.Id))),
            new CommandContextItem(new RemoteCommand("停止抓包 / 解除终端等待", new("tasks.stop", task.Id))),
            new CommandContextItem(new LogPage(task)),
        ],
    };
    public void Dispose() => _query.Dispose();
}

internal sealed partial class LogPage : ContentPage
{
    private readonly LaunchTask _task;
    public LogPage(LaunchTask task) { _task = task; Title = task.Name; Name = "最近执行日志"; }
    public override IContent[] GetContent()
    {
        try { var reply = Task.Run(() => AgentClient.CallAsync(new("tasks.log", _task.Id))).GetAwaiter().GetResult(); return [new MarkdownContent("```text\n" + (reply.Message + "\n\n" + reply.Data).Replace("```", "ˋˋˋ", StringComparison.Ordinal) + "\n```")]; }
        catch (Exception ex) { return [new MarkdownContent(ex.Message)]; }
    }
}
