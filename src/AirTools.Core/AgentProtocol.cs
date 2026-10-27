using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AirTools.Core;

public sealed record AgentRequest(string Operation, string Id = "", string Query = "", string Data = "");
public sealed record ClipboardEntry(string Id, string Text, string Kind, bool Pinned, DateTime Time, string Thumbnail = "");
public sealed record AgentReply(bool Success, string Message = "", string Data = "");
public sealed record AgentOptions(bool RecordClipboard = true, int MaxHistory = 500);

public static class AgentProtocol
{
    public const int MaximumMessageBytes = 4 * 1024 * 1024;
    public static string DataDirectory => Environment.GetEnvironmentVariable("AIRTOOLS_DATA_DIRECTORY") is { Length: > 0 } path
        ? Path.GetFullPath(path) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirTools.CommandPalette");
    public static string PipeName => "AirTools.Agent." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName + "|" + DataDirectory.ToUpperInvariant())))[..24];

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumMessageBytes) throw new IOException("请求过大");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var count = BitConverter.ToInt32(header);
        if (count is < 1 or > MaximumMessageBytes) throw new IOException("消息长度无效");
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new IOException("消息无效");
    }

    public static async Task<AgentReply> SendAsync(AgentRequest request, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await WriteAsync(pipe, request, timeout.Token);
        return await ReadAsync<AgentReply>(pipe, timeout.Token);
    }
}
