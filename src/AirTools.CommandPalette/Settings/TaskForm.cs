using System.Text.Json;
using System.Globalization;
using System.Text.Json.Nodes;
using AirTools.Core;
using AirTools.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AirTools.CommandPalette;

internal sealed partial class TaskForm : FormContent
{
    private readonly LaunchTask _original;
    private readonly Action _refresh;
    public TaskForm(LaunchTask task, Action refresh, bool existing = false)
    {
        _original = task; _refresh = refresh;
        var actions = new JsonArray();
        var body = new JsonArray();
        if (existing)
        {
            var alias = string.IsNullOrWhiteSpace(task.Alias) ? "未设置搜索别名" : "别名：" + task.Alias;
            body.Add(new JsonObject { ["type"] = "Container", ["style"] = "emphasis", ["bleed"] = true, ["items"] = new JsonArray(Label(task.Name, true), Label($"{KindName(task.Kind)} · {alias}")) });
            actions.Add(Submit("启动", "run"));
            actions.Add(Show("编辑", Editor(task, true)));
            actions.Add(Submit("复制", "duplicate"));
            actions.Add(Show("删除", Card(new JsonArray(Label($"确定删除“{task.Name}”？此操作无法撤销。")), new JsonArray(Submit("确认删除", "delete")))));
        }
        else
        {
            body.Add(Label("新增任务", true));
            body.Add(Label("选择类型后填写配置，保存不会自动启动。"));
            foreach (var kind in Enum.GetValues<LaunchKind>())
                actions.Add(Show("＋ " + KindName(kind), Editor(new LaunchTask { Id = task.Id, Kind = kind, Name = "", Executable = kind == LaunchKind.SshCapture ? @"C:\Program Files\Wireshark\Wireshark.exe" : "" }, false)));
        }
        TemplateJson = Card(body, actions).ToJsonString();
    }    internal static string KindName(LaunchKind kind) => kind switch { LaunchKind.Program => "程序", LaunchKind.Terminal => "终端命令", LaunchKind.PowerShell => "PowerShell 脚本", _ => "SSH 抓包" };
    private static JsonObject Label(string text, bool heading = false) => new() { ["type"] = "TextBlock", ["text"] = text, ["wrap"] = true, ["weight"] = heading ? "Bolder" : "Default", ["size"] = heading ? "Medium" : "Small", ["isSubtle"] = !heading };
    private static JsonObject Card(JsonArray body, JsonArray actions) => new() { ["type"] = "AdaptiveCard", ["version"] = "1.5", ["body"] = body, ["actions"] = actions };
    private static JsonObject Submit(string title, string operation, LaunchKind? kind = null) => new() { ["type"] = "Action.Submit", ["title"] = title, ["associatedInputs"] = operation == "save" ? "auto" : "none", ["data"] = new JsonObject { ["operation"] = operation, ["kind"] = (int)(kind ?? LaunchKind.Program) } };
    private static JsonObject Show(string title, JsonObject card) => new() { ["type"] = "Action.ShowCard", ["title"] = title, ["card"] = card };
    private static JsonObject Editor(LaunchTask task, bool existing)
    {
        var body = new JsonArray(Label(KindName(task.Kind) + "设置", true));
        void Text(string id, string label, string value, bool multiline = false) => body.Add(new JsonObject { ["type"] = "Input.Text", ["id"] = id, ["label"] = label, ["value"] = value, ["isMultiline"] = multiline });
        Text("Name", "任务名称", task.Name); Text("Alias", "搜索别名（可选）", task.Alias);
        if (task.Kind != LaunchKind.PowerShell)
            Text("Executable", task.Kind == LaunchKind.SshCapture ? "Wireshark 程序路径" : "程序路径或命令（如 python.exe）", task.Executable);
        if (task.Kind is LaunchKind.Program or LaunchKind.Terminal)
        {
            var complex = task.Arguments.Any(value => value.Length == 0 || value.Contains('\n') || value.Contains('\r'));
            body.Add(Label(complex ? "此任务包含空参数或换行参数，使用 JSON 数组完整保留。" : "每行一个参数，无需加引号。空格属于该参数的一部分。"));
            Text(complex ? "ArgumentsJson" : "Arguments", complex ? "启动参数（JSON 数组）" : "启动参数", complex ? JsonSerializer.Serialize(task.Arguments) : string.Join("\n", task.Arguments), true);
        }
        if (task.Kind != LaunchKind.SshCapture) Text("WorkingDirectory", "工作目录（可选）", task.WorkingDirectory);
        if (task.Kind == LaunchKind.PowerShell) Text("Script", "PowerShell 脚本", task.Script, true);
        if (task.Kind is LaunchKind.Terminal or LaunchKind.PowerShell)
            body.Add(new JsonObject { ["type"] = "Input.ChoiceSet", ["id"] = "TerminalPolicy", ["label"] = "命令结束后", ["value"] = ((int)task.TerminalPolicy).ToString(CultureInfo.InvariantCulture), ["choices"] = new JsonArray(new JsonObject { ["title"] = "关闭终端", ["value"] = "0" }, new JsonObject { ["title"] = "仅失败时保留", ["value"] = "1" }, new JsonObject { ["title"] = "始终保留", ["value"] = "2" }) });
        if (task.Kind == LaunchKind.SshCapture)
        {
            body.Add(Label("首次连接需确认指纹。保存后可单独设置密码，密码不会显示在表单中。"));
            Text("Host", "SSH 主机", task.Host); Text("Port", "端口", task.Port.ToString(CultureInfo.InvariantCulture)); Text("Username", "用户名", task.Username);
            Text("PrivateKey", "私钥文件（可选）", task.PrivateKey); Text("Interface", "抓包接口", task.Interface); Text("Tcpdump", "远端 tcpdump 路径", task.Tcpdump);
            Text("CaptureFilter", "抓包过滤器（可清空；默认排除 SSH 流量）", task.CaptureFilter); Text("ExtraCaptureArguments", "额外 tcpdump 参数（可选）", task.ExtraCaptureArguments);
        }
        var actions = new JsonArray(Submit("保存", "save", task.Kind));
        if (existing && task.Kind == LaunchKind.SshCapture) { actions.Add(Submit("设置密码 / 私钥口令", "password")); actions.Add(Submit("测试连接", "test")); }
        return Card(body, actions);
    }
    public override ICommandResult SubmitForm(string inputs, string data)
    {
        try
        {
            var meta = JsonNode.Parse(data);
            var operation = meta?["operation"]?.ToString() ?? throw new ArgumentException("缺少操作类型");
            if (operation is "run" or "test" or "delete" or "password")
            {
                var reply = Task.Run(() => AgentClient.CallAsync(new("tasks." + operation, _original.Id))).GetAwaiter().GetResult();
                if (operation == "delete") _refresh();
                return CommandResult.ShowToast(string.IsNullOrEmpty(reply.Message) ? "操作完成" : reply.Message);
            }
            var task = JsonSerializer.Deserialize<LaunchTask>(JsonSerializer.Serialize(_original))!;
            if (operation == "duplicate")
            {
                task.Id = Guid.NewGuid().ToString("N"); task.Name += " 副本"; task.EncryptedPassword = ""; task.Fingerprint = "";
            }
            else if (operation == "save")
            {
                var json = JsonNode.Parse(inputs) ?? throw new ArgumentException("表单为空");
                string S(string key, string fallback) => json[key]?.ToString() ?? fallback;
                task.Kind = (LaunchKind)int.Parse(meta!["kind"]!.ToString(), CultureInfo.InvariantCulture);
                task.Name = S("Name", task.Name).Trim(); task.Alias = S("Alias", task.Alias);
                task.Executable = S("Executable", task.Executable).Trim(); task.WorkingDirectory = S("WorkingDirectory", task.WorkingDirectory).Trim();
                if (json["ArgumentsJson"] != null) task.Arguments = JsonSerializer.Deserialize<string[]>(S("ArgumentsJson", "[]")) ?? throw new ArgumentException("参数必须为 JSON 数组");
                else if (json["Arguments"] != null) task.Arguments = S("Arguments", "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                task.Script = S("Script", task.Script);
                task.TerminalPolicy = (TerminalPolicy)int.Parse(S("TerminalPolicy", ((int)task.TerminalPolicy).ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
                task.Host = S("Host", task.Host).Trim(); task.Port = int.Parse(S("Port", task.Port.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
                task.Username = S("Username", task.Username); task.PrivateKey = S("PrivateKey", task.PrivateKey);
                task.Interface = S("Interface", task.Interface); task.Tcpdump = S("Tcpdump", task.Tcpdump); task.CaptureFilter = S("CaptureFilter", task.CaptureFilter); task.ExtraCaptureArguments = S("ExtraCaptureArguments", task.ExtraCaptureArguments);
            }
            else throw new ArgumentException("不支持的任务操作");
            LauncherSettings.Validate(task);
            var saved = Task.Run(() => AgentClient.CallAsync(new("tasks.save", Data: JsonSerializer.Serialize(task)))).GetAwaiter().GetResult();
            if (!saved.Success) return CommandResult.ShowToast("保存失败：" + saved.Message);
            _refresh(); return CommandResult.ShowToast(operation == "duplicate" ? "已创建副本（不复制密码和主机信任）" : "任务已保存");
        }
        catch (Exception ex) { return CommandResult.ShowToast("操作失败：" + ex.Message); }
    }
}



