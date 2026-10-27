# AirTools — Windows 命令面板扩展

只专注于 **剪切板管理** 与 **快速任务**。使用微软官方 Command Palette 扩展接口，无需二开、同步或重新编译 PowerToys。

作者：[liu.liuy@qq.com](mailto:liu.liuy@qq.com) · [GitHub](https://github.com/LIU-LIU-LIU/AirTools)

## 分支与目录

- `main`：原有独立应用完整源码，保留单 EXE 版本，基线提交 `bac3d61`。
- `codex/command-palette-extension`：仅插件及其后台，不再包含独立搜索框、系统监控或旧设置窗口。

```text
AirTools.sln
src/
  AirTools.Core/              任务模型、存储、搜索排序、管道协议
  AirTools.Agent/             剪切板监听、任务执行、SSH 抓包、图片查看器
  AirTools.CommandPalette/    官方扩展入口、原生列表/表单/设置、MSIX 清单与图标
scripts/                     构建、测试和本地注册
tests/AirTools.Tests/         Windows 隔离行为与集成检查
docs/                        架构与验证记录
```

## 使用方式

安装/开发注册后，在命令面板执行扩展重新加载，搜索 `AirTools`：

- **AirTools 剪切板**：输入即搜索，按分类筛选；支持置顶、删除、清空非置顶。Enter 复制并尝试粘贴至唤出前窗口，Ctrl+Enter 仅复制；图片 Ctrl+P 打开完整缩放/拖动预览。
- **AirTools 快速任务**：进入后按名称或别名搜索和运行；支持程序、可见终端、PowerShell 脚本、Wireshark SSH 抓包。

主页只注册剪切板和快速任务两个入口。关于信息、剪切板记录开关、历史数量及任务新增/编辑/复制/删除均在 **命令面板设置 → 扩展 → AirTools → 扩展设置**。任务仅在进入快速任务后搜索，不会单独出现在主页。停止后台记录与抓包放在扩展设置页底部。激活快捷键、外观、别名交给宿主设置，不再注册第二套全局快捷键，也没有额外托盘图标。可为两个入口配置别名，需避免和宿主内置命令冲突。公开扩展 API 不提供 PowerToys 主设置导航栏的自定义模块入口。

后台在首次访问插件时启动，之后持续监听剪切板；本版不自动添加登录启动项。关闭命令面板不会停止记录；可在扩展设置暂停记录或停止后台。宿主未提供粘贴目标 HWND，Agent 使用前台窗口事件记录目标，只有原窗口焦点可确认才发送粘贴。首次加载时尚未捕获目标、目标关闭、管理员权限边界或焦点无法恢复时会仅复制；可在刷新列表返回的运行状态中查看原因，再手动粘贴。

## 任务配置

程序路径和参数分开填写；参数为 JSON 数组，例如 `[".\\adb_vpn_monitor.py"]`。普通参数按字面值传递；需要管道或组合命令时选择 PowerShell 脚本类型。工作目录独立设置，保存不执行任务。

扩展设置页预置两个可编辑任务：

1. Python VPN 监控：`python.exe .\adb_vpn_monitor.py`，工作目录 `D:\Users\DELL\Desktop\android-work\QuasarVPN`。终端默认执行结束关闭，可设失败保留或始终保留；使用 UTF-8 输出并保存真实退出码。
2. Wireshark SSH：主机 `192.168.30.13:22`、用户 `root`、接口 `any`。先修正 Wireshark 路径，保存后在扩展设置的任务表单里设置密码/私钥口令。首次连接单独确认 SHA256 主机指纹，指纹变化拒绝连接。通过 SSH.NET 运行远端 tcpdump，再经唯一命名管道交给 Wireshark，不依赖本地 sshdump。默认 `not tcp port 22` 排除传输，可编辑/清空；更改 SSH 端口时请同步过滤器。

远端需要 tcpdump 和抓包权限。SSH 额外参数为远端 shell 参数，只填写自己信任的命令。上下文菜单提供连接测试、停止抓包、最近日志；停止终端任务仅解除后台等待，不强制杀掉用户脚本。普通程序不跟踪退出码，终端任务保存最近退出码和最多 256 行、每行 4096 字符的日志；界面最多读取最近 64K 字符。复制任务不会复制密码或已信任指纹。

## 数据与隐私

数据位于 `%LOCALAPPDATA%\AirTools.CommandPalette`：

- `launcher.json`：任务；`agent.json` 与 `command-palette-settings.json`：设置。
- `Clipboard/history.json`：文本/文件历史及置顶；它们为本地明文，请按需清理。
- `Logs/`：终端输出和退出码。命令输出可能包含敏感数据。
- 图片仅在 Agent 会话内存保存，置顶也不会持久化。压缩原图预算 128 MB，优先淘汰旧的非置顶图片；解码 LRU 预算 64 MB，列表只传小缩略图。复杂缩略图过大时仅显示图片标记，仍可完整预览。

首次启动从旧 `%LOCALAPPDATA%\AirTools` 导入文本/文件历史与任务，保留 `.import-backup`，不覆盖已有插件文件，不修改旧目录。通过 `.imported` 标记只导入一次。密码用当前 Windows 用户 DPAPI 加密，IPC 限当前用户访问，扩展列表不接收密码明文或密文。测试可设 `AIRTOOLS_DATA_DIRECTORY` 隔离数据。

## 构建与本地开发

**无需先上微软商店。** 可以先本地构建、开发注册、调试和重载；正式完成后再决定商店或 WinGet 分发。

构建前准备 Windows SDK 26100、.NET 10 SDK、PowerShell 7；Agent/Core 使用 .NET 8。扩展沿用本机命令面板 0.11.11762.0 所带模板及 SDK 版本，版本固定在 `Directory.Packages.props`，不要盲目升级。构建脚本优先使用 `%LOCALAPPDATA%\AirTools-Dev\dotnet\dotnet.exe`，否则使用 PATH 中的 dotnet。

```powershell
.\scripts\test.ps1
.\scripts\build.ps1
.\scripts\deploy-local.ps1
```

默认 Release x64；构建及注册支持 `-Architecture ARM64`（ARM64 尚未实机验证）。构建先发布自包含 Agent，再将它与自包含扩展一起打成 **未签名** MSIX/MSIX Bundle，输出在 `dist/package-x64`。开发注册使用生成目录中的 `AppxManifest.xml`；完成后在命令面板执行重载扩展。无需将 DLL 手动复制到 PowerToys 安装目录。

本机已由用户启用开发者模式，并成功开发注册。更新后在命令面板重载扩展；普通 `dotnet build` 扩展项目不会自动发布 Agent，请使用构建脚本；调试器可附加 `AirTools.CommandPalette.exe` 与 `AirTools.Agent.exe`。重建已注册目录前请停止 Agent 并让宿主卸载扩展；不要移动已注册的构建目录。

卸载开发包可运行 `Get-AppxPackage AirTools.CommandPalette | Remove-AppxPackage`。先停止后台；用户数据独立保存，需要时自行备份/清理。旧独立版仍保留在 main，建议测试插件时退出旧版，避免重复采集。

## 发布

当前清单 `CN=AirTools` 为开发身份，未签名包不是可直接安装的正式发行包。本地开发无需 Partner Center。上架前需注册商店应用、使用分配的 Identity/Publisher/PublisherDisplayName，按官方要求打包和验证，准备说明、隐私声明与素材；正式侧载则需要适当签名。WinGet 是官方说明的另一分发方式；扩展库提供分发链接，不代替打包/发布。

- [扩展开发总览](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/extensibility-overview)
- [发布扩展](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/publish-extension)
- [发布到商店](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/publish-extension-store)

## 验证状态

见 [验证记录](docs/validation.md) 与 [架构说明](docs/command-palette-migration.md)。隔离测试已通过，构建包已生成；宿主已确认两个入口及设置表单展示；表单提交/别名/粘贴/跨屏 DPI、真实 SSH 抓包仍需继续实测。资源数字只包含 Agent，不把它当作整个宿主占用。

微软官方模板的适配代码保留版权声明，许可见 [第三方声明](THIRD_PARTY_NOTICES.md)。
