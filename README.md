# AirTools

[Windows 命令面板（PowerToys Command Palette）](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/overview)扩展，提供 **剪切板历史管理** 与 **快速任务启动**。

基于微软官方扩展接口实现，无需修改或重新编译 PowerToys。

## 功能

### 剪切板

- 后台记录文本、链接、文件和图片，输入即搜索。
- 按类型筛选：全部、置顶、文本、链接、图片、文件。
- 置顶、删除、清空非置顶历史。
- `Enter` 粘贴到唤出前的窗口，`Ctrl+Enter` 仅复制。
- 图片在详情栏显示缩略图，`Ctrl+P` 打开可缩放、可拖动的完整预览。
- 链接（`http`、`https`、`ftp` 等 `scheme://`）可直接用系统默认应用打开。

### 快速任务

- 按名称或别名搜索并运行。
- 支持四种任务：程序、终端命令、PowerShell 脚本、Wireshark SSH 远程抓包。
- 任务的新增、编辑、复制、删除在 **命令面板设置 → 扩展 → AirTools** 中完成。

## 安装

### 环境要求

- Windows 11，已安装 [PowerToys](https://github.com/microsoft/PowerToys) 的命令面板（Command Palette）。
- x64（ARM64 可构建，尚未实机验证）。

### 从 GitHub Release 安装

1. 在 [Releases](https://github.com/LIU-LIU-LIU/AirTools/releases) 下载 `AirTools-<版本>-x64.msix`、`AirTools.cer` 和 `Install.ps1`，放在同一目录。
2. 右键 `Install.ps1` → **使用 PowerShell 运行**，允许管理员权限。脚本会信任发布证书并安装扩展。
3. 打开命令面板，执行 **重新加载（Reload）**，搜索 `AirTools`。

> 发行包使用自签名证书，因此需要先信任 `AirTools.cer`。只从本仓库的 Release 下载，并用 `SHA256SUMS.txt` 校验文件。

卸载：

```powershell
Get-AppxPackage AirTools.CommandPalette | Remove-AppxPackage
```

## 使用

在命令面板主页搜索：

| 入口 | 说明 |
| --- | --- |
| AirTools 剪切板 | 搜索、筛选并粘贴历史记录 |
| AirTools 快速任务 | 搜索并运行已配置的任务 |

扩展设置位于 **命令面板设置 → 扩展 → AirTools**，包含剪切板记录开关、历史数量（10–5000）、任务管理和“停止后台”。可在宿主设置中为两个入口配置别名与快捷键。

### 任务配置

- 程序路径与参数分开填写，参数每行一个，无需加引号。
- 需要管道或组合命令时，选择 PowerShell 脚本类型。
- 终端类任务可选择结束后关闭、仅失败时保留或始终保留终端。
- 复制任务不会复制密码和已信任的主机指纹。

### SSH 抓包

通过 SSH.NET 在远端运行 `tcpdump`，经命名管道交给 Wireshark，不依赖本地 `sshdump`。

- 填写 Wireshark 路径、主机、端口、用户和接口，保存后在任务表单中设置密码或私钥口令。
- 首次连接需确认 SHA256 主机指纹，指纹变化时拒绝连接。
- 默认过滤器 `not tcp port 22`，可修改或清空；更改 SSH 端口时请同步修改。
- 远端需要 `tcpdump` 及抓包权限。额外参数会交给远端 shell，只填写可信内容。

## 数据与隐私

数据保存在 `%LOCALAPPDATA%\AirTools.CommandPalette`：

| 内容 | 位置 | 说明 |
| --- | --- | --- |
| 任务 | `launcher.json` | 密码使用当前 Windows 用户的 DPAPI 加密 |
| 设置 | `agent.json`、`command-palette-settings.json` | |
| 文本/文件历史 | `Clipboard/history.json` | 本地明文，请按需清理 |
| 终端日志 | `Logs/` | 命令输出可能含敏感信息 |

- 图片只保存在后台进程内存中，置顶图片也不会写入磁盘；停止后台后图片历史丢失。
- 后台与扩展之间的通信仅限当前用户访问。
- 首次启动会从旧版 `%LOCALAPPDATA%\AirTools` 导入文本、文件历史和任务，不修改旧目录。

## 从源码构建

需要 Windows SDK 26100、.NET 10 SDK、PowerShell 7。Agent 与 Core 目标为 .NET 8。

```powershell
.\scripts\test.ps1             # 隔离环境行为测试
.\scripts\build.ps1            # 构建未签名 MSIX，输出到 dist\package-x64
.\scripts\deploy-local.ps1     # 开发注册到本机并请求重载
```

- 默认 Release x64，可加 `-Architecture ARM64`。
- 开发注册需在 Windows 开启开发者模式；`x-cmdpal://reload` 需在命令面板设置中允许外部重新加载。
- 同版本重新注册会失败，需先卸载开发包，或提升 `Package.appxmanifest` 中的版本号。
- 不要移动已注册的构建目录；重建前先停止后台。

## 打包与发布

### 在 GitHub 发布（自签名，无需上架）

```powershell
.\scripts\package-release.ps1
```

脚本依次执行：构建、签名、生成发布文件。输出在 `dist\release-x64`：

- `AirTools-<版本>-x64.msix`：已签名安装包
- `AirTools.cer`：公钥证书
- `Install.ps1`：信任证书并安装
- `SHA256SUMS.txt`：校验值

签名证书首次运行时生成于 `%LOCALAPPDATA%\AirTools-Dev\signing`，私钥与口令只保存在本机，请勿提交到仓库。之后的版本需用同一个证书，否则用户需要重新信任。

发布步骤：

1. 修改 `src/AirTools.CommandPalette/Package.appxmanifest` 的 `Identity Version`，例如 `0.0.2.0`。
2. 运行 `.\scripts\package-release.ps1`。
3. 在 GitHub 新建 Release，上传 `dist\release-x64` 中的四个文件。

### 上架或 WinGet

自签名包仅用于自行分发。上架 Microsoft Store 需要注册开发者账号，并使用商店分配的 Identity、Publisher 与 PublisherDisplayName，见官方文档：

- [扩展开发总览](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/extensibility-overview)
- [发布扩展](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/publish-extension)
- [发布到商店](https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/publish-extension-store)

## 项目结构

```text
src/
  AirTools.Core/              任务模型、存储、搜索排序、通信协议
  AirTools.Agent/             剪切板监听、任务执行、SSH 抓包、图片查看器
  AirTools.CommandPalette/    扩展入口、列表/表单/设置页、MSIX 清单
scripts/                      构建、测试、本地注册、发布打包
tests/AirTools.Tests/         行为与集成检查
docs/                         架构与验证记录
```

更多说明见 [架构说明](docs/command-palette-migration.md) 与 [验证记录](docs/validation.md)。

## 已知限制

- 详情栏图片的显示尺寸由宿主控制，需要看大图请使用 `Ctrl+P`。
- 粘贴依赖前台窗口事件记录的目标窗口；目标关闭、跨管理员权限或焦点无法恢复时仅复制。
- 表单提交、别名、跨屏 DPI 与真实 SSH 抓包仍需更多实机验证。

## 许可

微软官方模板的适配代码保留版权声明，见 [第三方声明](THIRD_PARTY_NOTICES.md)。

项目地址：[https://github.com/LIU-LIU-LIU/AirTools](https://github.com/LIU-LIU-LIU/AirTools)
