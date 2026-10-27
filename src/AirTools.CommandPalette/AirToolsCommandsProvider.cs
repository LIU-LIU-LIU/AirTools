using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

public sealed partial class AirToolsCommandsProvider : CommandProvider, IDisposable
{
    private readonly ExtensionSettings _settings = new();
    private readonly ClipboardPage _clipboard;
    private readonly TasksPage _tasks;
    public AirToolsCommandsProvider()
    {
        Id = "AirTools.CommandPalette";
        DisplayName = "AirTools";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Settings = _settings;
        _clipboard = new(); _tasks = new();
        _settings.Attach(_tasks);
    }

    public override ICommandItem[] TopLevelCommands() => [
        new CommandItem(_clipboard) { Title = "AirTools 剪切板", Subtitle = "搜索历史、置顶、分类与图片预览" },
        new CommandItem(_tasks) { Title = "AirTools 快速任务", Subtitle = "程序、终端命令与 SSH 抓包" },
    ];
    public override void Dispose() { _settings.Dispose(); _clipboard.Dispose(); _tasks.Dispose(); base.Dispose(); }
}
