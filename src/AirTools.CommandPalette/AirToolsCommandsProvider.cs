using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

public sealed partial class AirToolsCommandsProvider : CommandProvider, IDisposable
{
    private readonly ExtensionSettings _settings = new();
    private readonly ClipboardPage _clipboard = new();
    private readonly TasksPage _tasks = new();
    public AirToolsCommandsProvider()
    {
        Id = "AirTools.CommandPalette";
        DisplayName = "AirTools";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Settings = _settings;
    }

    // One entry point for quick commands (search by name / alias) and one for clipboard history.
    public override ICommandItem[] TopLevelCommands() => [
        new CommandItem(_tasks) { Title = "AirTools 快速命令", Subtitle = "按名称或别名搜索并运行已保存的任务" },
        new CommandItem(_clipboard) { Title = "AirTools 剪切板", Subtitle = "搜索历史并粘贴、置顶" },
    ];
    public override void Dispose() { _clipboard.Dispose(); _tasks.Dispose(); base.Dispose(); }
}
