using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

internal sealed class ExtensionSettings : JsonSettingsManager, ICommandSettings, IDisposable
{
    private readonly TaskManagementSettingsPage _page;
    private TasksPage? _tasks;
    public IContentPage SettingsPage => _page;

    public ExtensionSettings()
    {
        _page = new TaskManagementSettingsPage(this);
        FilePath = System.IO.Path.Combine(AirTools.Core.AgentProtocol.DataDirectory, "command-palette-settings.json");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        Settings.Add(new ToggleSetting("recordClipboard", "记录剪切板", "后台记录文本、文件和图片；图片仅在本次后台会话保存", true));
        Settings.Add(new TextSetting("maxHistory", "历史数量", "10–5000；图片压缩数据内存上限 128 MB", "500"));
        LoadSettings();
        Settings.SettingsChanged += (_, _) => { SaveSettings(); _ = ApplyAsync(); _page.Refresh(); };
        AgentClient.TasksChanged += TasksChanged;
        _ = ApplyAsync();
    }

    public void Attach(TasksPage tasks) => _tasks = tasks;
    private void TasksChanged() { _page.Refresh(); _tasks?.Refresh(); }
    private async Task ApplyAsync()
    {
        try
        {
            var count = int.TryParse(Settings.GetSetting<string>("maxHistory"), out var value) ? value : 500;
            if (count is < 10 or > 5000) { new ToastStatusMessage("历史数量应为 10–5000").Show(); return; }
            await AgentClient.CallAsync(new("options", Data: System.Text.Json.JsonSerializer.Serialize(new AirTools.Core.AgentOptions(Settings.GetSetting<bool>("recordClipboard"), count))));
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }
    public void Dispose() => AgentClient.TasksChanged -= TasksChanged;
}

internal sealed partial class TaskManagementSettingsPage : ContentPage
{
    private readonly ExtensionSettings _settings;
    public TaskManagementSettingsPage(ExtensionSettings settings)
    {
        _settings = settings; Id = "airtools.settings"; Title = "AirTools 设置"; Name = "设置";
        Icon = new IconInfo("\uE713");
    }

    public override IContent[] GetContent()
    {
        var content = new List<IContent>(_settings.Settings.ToContent())
        {
            new MarkdownContent("## 快速任务\n\n在此新增、编辑或删除任务。任务只会在命令面板主页进入“AirTools 快速任务”后搜索和运行。"),
        };
        try
        {
            var reply = Task.Run(() => AgentClient.CallAsync(new("tasks.list"))).GetAwaiter().GetResult();
            foreach (var task in AgentClient.Read<List<AirTools.Services.LaunchTask>>(reply))
                content.Add(new TaskForm(task, Refresh, existing: true));
            content.Add(new TaskForm(new AirTools.Services.LaunchTask(), Refresh));
        }
        catch (Exception ex) { content.Add(new MarkdownContent("无法读取任务：" + ex.Message)); }
        content.Add(new MarkdownContent("## 关于\n\nAirTools · liu.liuy@qq.com\n\n[GitHub 项目](https://github.com/LIU-LIU-LIU/AirTools)"));
        content.Add(new BackgroundControlForm());
        return content.ToArray();
    }

    public void Refresh() => RaiseItemsChanged();
}

internal sealed partial class BackgroundControlForm : FormContent
{
    public BackgroundControlForm() => TemplateJson = """
    {"type":"AdaptiveCard","version":"1.5","body":[{"type":"TextBlock","text":"停止后台会停止记录并释放抓包，本次会话图片历史将丢失。再次访问 AirTools 将重新启动。","wrap":true}],"actions":[{"type":"Action.ShowCard","title":"停止后台…","card":{"type":"AdaptiveCard","body":[{"type":"TextBlock","text":"确定停止后台并丢弃会话图片？","wrap":true}],"actions":[{"type":"Action.Submit","title":"确认停止","associatedInputs":"none"}]}}]}
    """;
    public override ICommandResult SubmitForm(string inputs, string data)
    {
        try { Task.Run(() => AgentClient.CallAsync(new("shutdown"))).GetAwaiter().GetResult(); return CommandResult.ShowToast("AirTools 后台已停止"); }
        catch (Exception ex) { return CommandResult.ShowToast(ex.Message); }
    }
}
