<div align="center">

<img src="src/AirTools/Assets/AirTools-512.png" width="96" alt="AirTools" />

# AirTools

**Windows 剪切板历史 · 晾衣架 · 快速命令 · 资源监控 —— 一个托盘程序，都在手边。**

[![Build](https://github.com/LIU-LIU-LIU/AirTools/actions/workflows/build.yml/badge.svg)](https://github.com/LIU-LIU-LIU/AirTools/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)

</div>

![AirTools 主页](docs/images-home.png)

## 它能做什么

| | |
| --- | --- |
| **晾衣架** | 最近复制的文字收在屏幕角落的一枚小按钮里。鼠标靠近，卡片沿弧线像 Dock 一样展开，点一下就粘贴到当前输入窗口，**不会抢走焦点**。可固定常用条目、拖动换位置、一键收起。 |
| **剪切板历史** | 文本、图片、文件统一记录，搜索、置顶、清理；条数、保留时长、是否记录图片/文件都可设置，也能随时暂停记录。 |
| **搜索启动器** | 一个快捷键唤出搜索栏：按名称或别名运行**快速命令**（程序、终端命令、PowerShell 脚本、SSH 远程抓包），或输入模式键搜索剪切板。 |
| **系统监控** | 置顶的小条，显示 CPU、内存、磁盘与网络速率；可拖动，自动吸附到屏幕边角（按住 Shift / Ctrl 不吸附）。 |
| **命令面板扩展** | 可选：安装到 PowerToys 命令面板，在里面直接搜索快速命令和剪切板历史。 |

![晾衣架与设置](docs/images-rack-settings.png)

## 安装

1. 到 [Releases](https://github.com/LIU-LIU-LIU/AirTools/releases) 下载 `AirTools-<版本>-win-x64.zip`，解压到任意目录。
2. 运行 `AirTools.exe`。自包含，**无需安装 .NET**。程序常驻系统托盘，右键托盘图标打开设置。
3. 想开机自启：设置 → 常规 → 开机自动启动。

系统要求：Windows 10 / 11（x64）。

### 安装命令面板扩展（可选）

需要已安装 [PowerToys](https://learn.microsoft.com/windows/powertoys/) 的命令面板，并开启 Windows“开发人员模式”（设置 → 系统 → 开发者选项）。然后：

> AirTools 设置 → **命令面板** → **安装到命令面板**

扩展包随 zip 一起提供（`AirTools-Extension-x64.zip`），没有时会自动从 Releases 下载。安装后在命令面板里执行一次 Reload，输入 `AirTools` 即可。扩展只是入口：它不保存任何数据，也不带后台进程，AirTools 需要保持运行。

## 默认快捷键

所有快捷键都可以在设置里重新录制或清除。

| 操作 | 快捷键 |
| --- | --- |
| 打开搜索启动器 | `Ctrl+Space` |
| 剪切板历史 | `Ctrl+Shift+V` |
| 显示 / 隐藏晾衣架 | `Ctrl+Alt+R` |
| 粘贴晾衣架第 1–5 件（1 为最新） | `Ctrl+Alt+1` … `Ctrl+Alt+5` |

## 使用小技巧

- **晾衣架**：单击角落按钮收起 / 展开；右键卡片可固定、取下；拖动按钮到屏幕任意位置，卡片会自动朝屏幕内侧展开。
- **搜索启动器**：空输入框里输入模式键（默认 `c`）进入剪切板搜索；`Ctrl+Enter` 仅复制，`Ctrl+P` 预览图片。
- **快速命令**：设置 → 快速启动里新增任务，可设置别名、独立快捷键；“结束运行”会结束任务启动的所有进程。

## 数据与隐私

- 配置：`%LOCALAPPDATA%\AirTools\launcher.json`；历史：`%LOCALAPPDATA%\AirTools\Clipboard\history.json`。设置 → 常规可以直接打开数据目录。
- 图片历史只保存在内存中，不写入磁盘。SSH 密码使用当前 Windows 用户的 DPAPI 加密。
- 程序不联网，只有点击“安装到命令面板”且本地没有扩展包时才会访问 GitHub Releases。

## 从源码构建

需要 Windows、.NET 8 SDK、PowerShell 7；构建命令面板扩展还需要 .NET 10 SDK。

```powershell
dotnet build AirTools.sln -c Release   # 构建
./scripts/test.ps1                      # 测试（使用临时数据目录）
./publish.ps1 -SelfContainedOnly        # 发布到 dist/self-contained/AirTools.exe
./scripts/package-extension.ps1         # 打包命令面板扩展到 dist/release/
./scripts/build-extension.ps1 -Register # 本地构建并以开发模式注册扩展
./scripts/make-icon.ps1                 # 重新生成图标
```

推送 `v*` 标签（如 `v1.0.0`）时，GitHub Actions 会自动构建、测试并把压缩包发布到 Releases。

## 目录结构

```
src/AirTools/                主程序（WPF + Win32）
  Services/                  应用宿主、快捷键、任务运行器、命令面板管道与安装器
  Tools/Clipboard/           剪切板历史与晾衣架
  Tools/SystemMonitor/       资源监控
src/AirTools.Core/           主程序与扩展共用的协议
src/AirTools.CommandPalette/ PowerToys 命令面板扩展（独立版的客户端）
tests/AirTools.Tests/        自动化测试
scripts/                     测试、打包、图标脚本
docs/                        截图与设计文档
```

设计与现状见 [docs/PLAN.md](docs/PLAN.md)，开发交接见 [docs/HANDOFF.md](docs/HANDOFF.md)，验证记录见 [VALIDATION.md](VALIDATION.md)。

## 许可证

[Apache-2.0](LICENSE)。第三方组件及许可证见 [NOTICE](NOTICE)、[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 与 [licenses/](licenses)。

致谢（交互与平台做法的参考，未复制源码）：[CopyQ](https://github.com/hluk/CopyQ)、[Ditto](https://github.com/sabrogden/Ditto)、[Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher)、[PowerToys](https://github.com/microsoft/PowerToys)、[WPF-UI](https://github.com/lepoco/wpfui)。
