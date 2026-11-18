using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

/// <summary>The extension has no options of its own; everything is configured in AirTools. This page just points there.</summary>
internal sealed class ExtensionSettings : ICommandSettings
{
    public IContentPage SettingsPage { get; } = new AboutPage();
}

internal sealed partial class AboutPage : ContentPage
{
    public AboutPage() { Id = "airtools.about"; Title = "AirTools"; Name = "设置"; Icon = new IconInfo(""); }

    public override IContent[] GetContent() =>
    [
        new OpenSettingsForm(),
        new MarkdownContent($"**AirTools** · Windows 剪切板历史、晾衣架与快速命令\n\n[{Core.AgentProtocol.Repository}]({Core.AgentProtocol.Repository})"),
    ];
}

/// <summary>One button that asks the running AirTools to open its settings window.</summary>
internal sealed partial class OpenSettingsForm : FormContent
{
    public OpenSettingsForm() => TemplateJson = """
    {"type":"AdaptiveCard","version":"1.5","body":[{"type":"TextBlock","text":"快速命令和剪切板的设置，请在 AirTools 中打开。","wrap":true}],"actions":[{"type":"Action.Submit","title":"打开 AirTools 设置","associatedInputs":"none"}]}
    """;

    public override ICommandResult SubmitForm(string inputs, string data)
    {
        try { Task.Run(() => AgentClient.CallAsync(new("ui.settings"))).GetAwaiter().GetResult(); return CommandResult.Dismiss(); }
        catch (Exception ex) { return CommandResult.ShowToast(ex.Message); }
    }
}
