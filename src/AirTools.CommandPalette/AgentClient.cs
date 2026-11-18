using System.Text.Json;
using AirTools.Core;

namespace AirTools.CommandPalette;

/// <summary>Talks to the running AirTools.exe. The extension owns no data and starts no background process.</summary>
internal static class AgentClient
{
    public static async Task<AgentReply> CallAsync(AgentRequest request, CancellationToken token = default)
    {
        AgentReply reply;
        try { reply = await AgentProtocol.SendAsync(request, token); }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException && !token.IsCancellationRequested)
        { throw new InvalidOperationException("未连接到 AirTools：请先运行 AirTools 独立版（托盘图标）。"); }
        if (!reply.Success) throw new InvalidOperationException(reply.Message);
        return reply;
    }

    public static T Read<T>(AgentReply reply) => JsonSerializer.Deserialize<T>(reply.Data) ?? throw new IOException("AirTools 返回内容无效");
}
