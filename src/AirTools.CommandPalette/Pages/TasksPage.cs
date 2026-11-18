using AirTools.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

/// <summary>"快速命令": search saved AirTools tasks by name or alias and run them. Editing happens in AirTools settings.</summary>
internal sealed partial class TasksPage : DynamicListPage, IDisposable
{
    private IListItem[] _items = [];
    private readonly LatestQuery _query = new();
    private long _lastRefresh;
    public TasksPage()
    {
        Id = "airtools.tasks"; Title = "AirTools 快速命令"; Name = "打开"; PlaceholderText = "搜索任务名称或别名…";
        Icon = new IconInfo(""); Refresh();
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
            var tasks = AgentClient.Read<List<TaskInfo>>(reply);
            if (token.IsCancellationRequested) return;
            var items = tasks.Select(x => (Task: x, Score: SearchRanking.Score(x.Name + " " + x.Alias, query))).Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(50).Select(x => Entry(x.Task)).ToList();
            if (items.Count == 0) items.Add(new ListItem(new RemoteCommand("打开 AirTools 设置", new("ui.settings", Data: "快速启动"), dismiss: true)) { Title = string.IsNullOrWhiteSpace(query) ? "还没有任务" : "没有匹配的任务", Subtitle = "在 AirTools 设置 → 快速启动 中新增或编辑", Icon = new IconInfo("") });
            _query.TryPublish(token, () => { _items = items.ToArray(); RaiseItemsChanged(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _query.TryPublish(token, () => { _items = [new ListItem(new ActionCommand("重试", Refresh)) { Title = "无法读取任务", Subtitle = ex.Message, Icon = new IconInfo("") }]; RaiseItemsChanged(); }); }
    }
    private ListItem Entry(TaskInfo task) => new ListItem(new RemoteCommand("运行", new("tasks.run", task.Id), dismiss: true))
    {
        Title = task.Name, Subtitle = (task.Running ? "运行中 · " : "") + (string.IsNullOrWhiteSpace(task.Alias) ? task.Kind : $"{task.Kind} · {task.Alias}"), Icon = new IconInfo(""),
        MoreCommands = [
            new CommandContextItem(new RemoteCommand("结束运行", new("tasks.stop", task.Id), Refresh)),
            new CommandContextItem(new LogPage(task)),
            new CommandContextItem(new RemoteCommand("在 AirTools 中编辑", new("ui.settings", Data: "快速启动"), dismiss: true)),
        ],
    };
    public void Dispose() => _query.Dispose();
}

internal sealed partial class LogPage : ContentPage
{
    private readonly TaskInfo _task;
    public LogPage(TaskInfo task) { _task = task; Title = task.Name; Name = "最近执行日志"; }
    public override IContent[] GetContent()
    {
        try { var reply = Task.Run(() => AgentClient.CallAsync(new("tasks.log", _task.Id))).GetAwaiter().GetResult(); return [new MarkdownContent("```text\n" + reply.Data.Replace("```", "ˋˋˋ", StringComparison.Ordinal) + "\n```")]; }
        catch (Exception ex) { return [new MarkdownContent(ex.Message)]; }
    }
}
