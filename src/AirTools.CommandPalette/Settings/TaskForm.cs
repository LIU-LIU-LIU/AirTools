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
    private readonly string _id;
    private readonly Action _refresh;
    private readonly string _icon;
    public TaskForm(LaunchTask task, Action refresh, bool existing = false)
    {
        _id = task.Id; _refresh = refresh; _icon = task.Icon;
        var body = new JsonArray();
        body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = "配置只会保存，点击任务才会执行。参数使用 JSON 数组，脚本与程序参数分开。", ["wrap"] = true });
        void Text(string id, string label, string value, bool multiline = false) => body.Add(new JsonObject { ["type"] = "Input.Text", ["id"] = id, ["label"] = label, ["value"] = value, ["isMultiline"] = multiline });
        void Choice(string id, string label, string value, params (string title, string value)[] choices)
        {
            var array = new JsonArray(); foreach (var item in choices) array.Add(new JsonObject { ["title"] = item.title, ["value"] = item.value });
            body.Add(new JsonObject { ["type"] = "Input.ChoiceSet", ["id"] = id, ["label"] = label, ["value"] = value, ["choices"] = array });
        }
        Text("Name", "名称", task.Name); Text("Alias", "搜索别名", task.Alias);
        Choice("Kind", "类型", ((int)task.Kind).ToString(CultureInfo.InvariantCulture), ("程序", "0"), ("终端命令", "1"), ("PowerShell 脚本", "2"), ("Wireshark SSH 抓包", "3"));
        Text("Executable", "程序 / Python / Wireshark 路径", task.Executable);
        Text("Arguments", "参数 JSON 数组，例如 [\".\\\\adb_vpn_monitor.py\"]", JsonSerializer.Serialize(task.Arguments), true);
        Text("WorkingDirectory", "工作目录", task.WorkingDirectory);
        Text("Script", "PowerShell 脚本文本（只用于脚本类型）", task.Script, true);
        Choice("TerminalPolicy", "终端关闭策略", ((int)task.TerminalPolicy).ToString(CultureInfo.InvariantCulture), ("结束即关闭", "0"), ("失败时保留", "1"), ("始终保留", "2"));
        body.Add(new JsonObject { ["type"] = "TextBlock", ["text"] = "SSH 抓包设置：首次连接需确认主机指纹。密码在保存任务后单独设置，不显示在表单中。", ["wrap"] = true });
        Text("Host", "SSH 主机", task.Host); Text("Port", "端口", task.Port.ToString(CultureInfo.InvariantCulture)); Text("Username", "用户名", task.Username);
        Text("PrivateKey", "私钥文件（可选）", task.PrivateKey); Text("Interface", "接口", task.Interface); Text("Tcpdump", "远端 tcpdump 路径", task.Tcpdump);
        Text("CaptureFilter", "抓包过滤器（默认排除 SSH 传输，可修改或清空）", task.CaptureFilter);
        Text("ExtraCaptureArguments", "额外 tcpdump 参数（远端 shell 参数）", task.ExtraCaptureArguments);
        var actions = new JsonArray(new JsonObject { ["type"] = "Action.Submit", ["title"] = "保存任务", ["data"] = new JsonObject { ["operation"] = "save" } });
        if (existing)
        {
            actions.Add(new JsonObject { ["type"] = "Action.Submit", ["title"] = "复制为新任务", ["data"] = new JsonObject { ["operation"] = "duplicate" } });
            actions.Add(new JsonObject { ["type"] = "Action.Submit", ["title"] = "设置 SSH 密码 / 私钥口令", ["associatedInputs"] = "none", ["data"] = new JsonObject { ["operation"] = "password" } });
            actions.Add(new JsonObject
            {
                ["type"] = "Action.ShowCard", ["title"] = "删除任务…",
                ["card"] = new JsonObject
                {
                    ["type"] = "AdaptiveCard", ["body"] = new JsonArray(new JsonObject { ["type"] = "TextBlock", ["text"] = "删除后无法撤销。确定删除此任务？", ["wrap"] = true }),
                    ["actions"] = new JsonArray(new JsonObject { ["type"] = "Action.Submit", ["title"] = "确认删除", ["style"] = "destructive", ["associatedInputs"] = "none", ["data"] = new JsonObject { ["operation"] = "delete" } }),
                },
            });
        }
        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard", ["version"] = "1.5", ["body"] = body,
            ["actions"] = actions,
        };
        TemplateJson = new JsonObject
        {
            ["type"] = "AdaptiveCard", ["version"] = "1.5",
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "Action.ShowCard", ["title"] = existing ? "编辑 · " + task.Name : "＋ 新增快速任务", ["card"] = card }),
        }.ToJsonString();
    }
    public override ICommandResult SubmitForm(string inputs, string data)
    {
        try
        {
            var operation = string.IsNullOrWhiteSpace(data) ? "save" : JsonNode.Parse(data)?["operation"]?.ToString() ?? "save";
            if (operation is "delete" or "password")
            {
                Task.Run(() => AgentClient.CallAsync(new("tasks." + operation, _id))).GetAwaiter().GetResult();
                _refresh(); return CommandResult.KeepOpen();
            }
            var json = JsonNode.Parse(inputs) ?? throw new ArgumentException("表单为空");
            string S(string key) => json[key]?.ToString() ?? "";
            var task = new LaunchTask
            {
                Id = _id, Icon = _icon, Name = S("Name").Trim(), Alias = S("Alias"), Kind = (LaunchKind)int.Parse(S("Kind"), CultureInfo.InvariantCulture),
                Executable = S("Executable").Trim(), Arguments = JsonSerializer.Deserialize<string[]>(S("Arguments")) ?? [],
                WorkingDirectory = S("WorkingDirectory").Trim(), Script = S("Script"), TerminalPolicy = (TerminalPolicy)int.Parse(S("TerminalPolicy"), CultureInfo.InvariantCulture),
                Host = S("Host").Trim(), Port = int.Parse(S("Port"), CultureInfo.InvariantCulture), Username = S("Username"), PrivateKey = S("PrivateKey"),
                Interface = S("Interface"), Tcpdump = S("Tcpdump"), CaptureFilter = S("CaptureFilter"), ExtraCaptureArguments = S("ExtraCaptureArguments"),
            };
            LauncherSettings.Validate(task);
            if (operation == "duplicate") { task.Id = Guid.NewGuid().ToString("N"); task.Name += " 副本"; }
            Task.Run(() => AgentClient.CallAsync(new("tasks.save", Data: JsonSerializer.Serialize(task)))).GetAwaiter().GetResult();
            _refresh(); return CommandResult.ShowToast("任务已保存");
        }
        catch (Exception ex) { new ToastStatusMessage("未保存：" + ex.Message).Show(); return CommandResult.KeepOpen(); }
    }
}
