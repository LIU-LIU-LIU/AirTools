using System.Diagnostics;
using System.Text.Json;
using AirTools.Core;

namespace AirTools.CommandPalette;

internal static class AgentClient
{
    private static readonly SemaphoreSlim LaunchGate = new(1, 1);
    public static event Action? TasksChanged;
    public static async Task<AgentReply> CallAsync(AgentRequest request, CancellationToken token = default)
    {
        await EnsureAgentAsync(token);
        var reply = await AgentProtocol.SendAsync(request, token);
        if (!reply.Success) throw new InvalidOperationException(reply.Message);
        if (request.Operation is "tasks.save" or "tasks.delete") TasksChanged?.Invoke();
        return reply;
    }

    private static async Task EnsureAgentAsync(CancellationToken token)
    {
        await LaunchGate.WaitAsync(token);
        try
        {
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(token);
            probe.CancelAfter(150);
            try { await AgentProtocol.SendAsync(new("status"), probe.Token); return; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (IOException) { }
            var executable = Path.Combine(AppContext.BaseDirectory, "Agent", "AirTools.Agent.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("后台未打包，请使用 AirTools 插件安装包", executable);
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
        }
        finally { LaunchGate.Release(); }
    }

    public static T Read<T>(AgentReply reply) => JsonSerializer.Deserialize<T>(reply.Data) ?? throw new IOException("后台返回内容无效");
}
