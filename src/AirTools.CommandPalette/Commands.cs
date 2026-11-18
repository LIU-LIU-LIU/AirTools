using AirTools.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

internal sealed partial class ActionCommand : InvokableCommand
{
    private readonly Action _action;
    public ActionCommand(string name, Action action) { Name = name; _action = action; }
    public override ICommandResult Invoke() { _action(); return CommandResult.KeepOpen(); }
}

internal sealed partial class RemoteCommand : InvokableCommand
{
    private readonly AgentRequest _request;
    private readonly Action? _refresh;
    private readonly bool _dismiss;
    public RemoteCommand(string name, AgentRequest request, Action? refresh = null, bool dismiss = false)
    { Id = "airtools." + request.Operation + "." + request.Id; Name = name; _request = request; _refresh = refresh; _dismiss = dismiss; }
    public override ICommandResult Invoke()
    {
        try
        {
            // Copy/paste must acknowledge successful clipboard ownership before dismissing the host.
            var reply = Task.Run(() => AgentClient.CallAsync(_request)).GetAwaiter().GetResult();
            _refresh?.Invoke();
            return _dismiss ? CommandResult.Dismiss() : CommandResult.KeepOpen();
        }
        catch (Exception ex) { return CommandResult.ShowToast(ex.Message); }
    }
}

internal sealed partial class ConfirmRemoteCommand : InvokableCommand
{
    private readonly AgentRequest _request;
    private readonly Action? _refresh;
    public ConfirmRemoteCommand(string name, AgentRequest request, Action? refresh = null)
    { Name = name; _request = request; _refresh = refresh; }
    public override ICommandResult Invoke() => CommandResult.Confirm(new ConfirmationArgs
    {
        Title = Name, Description = "此操作无法撤销。是否继续？",
        PrimaryCommand = new RemoteCommand("确认", _request, _refresh),
    });
}
