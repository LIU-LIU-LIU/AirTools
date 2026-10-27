using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

public sealed partial class AirToolsCommandsProvider : CommandProvider, IDisposable
{
    private readonly ExtensionSettings _settings = new();
    private readonly ClipboardPage _clipboard;
    private readonly TasksPage _tasks;
    private ICommandItem[] _taskCommands = [];
    public AirToolsCommandsProvider()
    {
        Id = "AirTools.CommandPalette";
        DisplayName = "AirTools";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Settings = _settings.Settings;
        _clipboard = new(); _tasks = new();
        AgentClient.TasksChanged += TasksChanged;
        _ = LoadTasksAsync();
    }

    public override ICommandItem[] TopLevelCommands() => [
        new CommandItem(_clipboard) { Title = "AirTools 剪切板", Subtitle = "搜索历史、置顶、分类与图片预览" },
        new CommandItem(_tasks) { Title = "AirTools 快速任务", Subtitle = "程序、终端命令与 SSH 抓包" },
        new CommandItem(new AboutPage()) { Title = "关于 AirTools", Subtitle = "liu.liuy@qq.com" },
        new CommandItem(new ConfirmRemoteCommand("停止 AirTools 后台", new("shutdown"), () => { })) { Title = "AirTools 停止后台", Subtitle = "停止记录并释放抓包；再次访问 AirTools 会重新启动后台，图片历史将丢失" },
        .. _taskCommands,
    ];
    private void TasksChanged() => _ = LoadTasksAsync();
    public override void Dispose() { AgentClient.TasksChanged -= TasksChanged; _clipboard.Dispose(); _tasks.Dispose(); base.Dispose(); }

    private async Task LoadTasksAsync()
    {
        try
        {
            var reply = await AgentClient.CallAsync(new("tasks.list"));
            var tasks = AgentClient.Read<List<AirTools.Services.LaunchTask>>(reply);
            _taskCommands = tasks.Select(t => (ICommandItem)new CommandItem(new RemoteCommand("运行", new("tasks.run", t.Id), dismiss: true)) { Title = t.Name, Subtitle = "AirTools · " + t.Alias }).ToArray();
            RaiseItemsChanged();
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }
}

internal sealed class ExtensionSettings : JsonSettingsManager
{
    public ExtensionSettings()
    {
        FilePath = System.IO.Path.Combine(AirTools.Core.AgentProtocol.DataDirectory, "command-palette-settings.json");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        Settings.Add(new ToggleSetting("recordClipboard", "记录剪切板", "后台记录文本、文件和图片；图片仅在本次后台会话保存", true));
        Settings.Add(new TextSetting("maxHistory", "历史数量", "10–5000；图片压缩数据内存上限 128 MB", "500"));
        LoadSettings();
        Settings.SettingsChanged += (_, _) => { SaveSettings(); _ = ApplyAsync(); };
        _ = ApplyAsync();
    }
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
}

internal sealed partial class AboutPage : ContentPage
{
    public AboutPage() { Id = "airtools.about"; Title = "AirTools"; Name = "关于"; }
    public override IContent[] GetContent() => [new MarkdownContent("# AirTools\n\n剪切板管理与快速任务。\n\n作者：[liu.liuy@qq.com](mailto:liu.liuy@qq.com)\n\n项目：[LIU-LIU-LIU/AirTools](https://github.com/LIU-LIU-LIU/AirTools)\n\n使用官方 Command Palette 扩展接口，无需修改 PowerToys。")];
}
