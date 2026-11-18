# AirTools 交接文档

> 现状以 [README](../README.md) 和 [PLAN.md](PLAN.md) 为准；本文只记录开发过程中的坑与待验收项。

## 当前状态（2026-11-18）

- 主分支 `main`，不再使用其他分支开发。旧实验分支 `codex/command-palette-extension` 与 `independent-rebuild` 仅作历史保留。
- 功能：晾衣架（固定、拖动、右键菜单）、剪切板历史（条数 / 保留 / 图片预算 / 暂停）、搜索启动器（快速命令 + 剪切板搜索）、系统监控（吸附）、命令面板扩展（独立版安装 / 卸载）。
- 扩展是独立版的客户端：`src/AirTools/Services/PipeServer.cs` 是服务端，`src/AirTools.CommandPalette` 是客户端，协议在 `src/AirTools.Core`。
- 构建 / 测试 / 发布：见 README“从源码构建”。CI 在 `.github/workflows/build.yml`，推送 `v*` 标签自动发布 Release。

## 已知坑

- 扩展构建需要 .NET 10 SDK（本机在 `%LOCALAPPDATA%\AirTools-Dev\dotnet`）；主程序用 .NET 8。
- 重新构建扩展前要先结束 `AirTools.CommandPalette.exe`，否则输出文件被占用。
- `-InteractivePaste` 会占用鼠标和前台窗口，目标窗口拿不到前台时会失败；这属于环境阻断，不要混进成功统计。
- 扩展用“开发人员模式 + 未签名注册”，不适合发布到 Microsoft Store。
- `Wpf.Ui` 的符号名要以 DLL 为准（XML 文档不列枚举成员）。

## 仍需人工验收

- 记事本、浏览器、VS Code、终端里点击卡片 / 命令面板回车粘贴的输入语义。
- 多屏、不同 DPI、管理员窗口下晾衣架拖动与吸附。
- 命令面板里 Reload 后的扩展页面与“打开 AirTools 设置”按钮。

---

# 以下为早期交接记录（独立版重建阶段，部分内容已被上面的现状取代）

## 目标

AirTools 现在是独立 Windows 桌面程序，不再依赖 PowerToys、Command Palette、Dock 或 Agent。核心功能是剪切板历史、透明“晾衣架”悬浮条、全局快捷键、搜索、快速启动、SSH 抓包和系统监控。

## 本轮已完成

- 晾衣架改为透明背景：横杆、纯色衣物、衣物内文本；去掉字数、序号和常驻状态文字。
- 文本多时使用长衣物，显示只做自动换行和省略，点击仍粘贴完整原文。
- 夹子只在悬停时出现，点击后有下落和淡出动画；位置可以拖动并保存。
- 衣架窗口使用 `WS_EX_NOACTIVATE` / `MA_NOACTIVATE`，点击不会激活衣架。
- 衣物控件复用并按稳定 Id 调整顺序，避免复制或粘贴后整条衣架反复清空重建。
- 文本回写使用原生 Unicode 剪切板写入，避免 WPF/OLE 回调造成重复监听；自身写入增加序列号和内容双重抑制。
- 点击粘贴：标准 Edit/RichEdit 使用 `WM_PASTE`；其他输入控件在兼容模式使用 `Shift+Insert`。不在原生消息超时后再叠加第二种方式，防止重复粘贴。
- 增加不记录剪切板正文的诊断日志：`%LOCALAPPDATA%\\AirTools\\Logs\\paste.log`。
- 快捷键清空后不再注册；冲突会精确显示动作和组合键，允许其他可用快捷键继续保存。
- 设置页换成 WPF-UI FluentWindow + Mica + NavigationView + CardControl + ToggleSwitch；快捷键改为一行录制按钮和清除按钮。
- 发布仍为单个自包含 `AirTools.exe`，保留许可证和第三方依赖许可。

## 2026-11-18 UI 重做

- 晾衣架（`RackWindow.cs` 重写）：固定停靠在工作区右下角，像 macOS Dock 堆栈一样从角落圆形按钮沿弧线向上展开。闲置时是单行小卡片（彩虹色点区分），鼠标悬停的卡片放大并显示全文、快捷键提示和 × 取下，相邻卡片略大（Dock 放大效果），放大卡片锁定在指针下方不跳动。单击角落按钮可收起/展开（状态保存在 `RackCollapsed`），右键打开设置。新复制内容从按钮弹入；粘贴有按压动画和“✓ 已粘贴”反馈。顶部卡片打开完整剪切板历史。透明区域保持点击穿透，仍为 NOACTIVATE。旧的拖动位置 `RackLeft/RackTop` 不再使用（字段保留以兼容配置）。
- 设置（`LauncherSettingsWindow.cs` 重写）：仿 PowerToys 设置——分组导航（概览/工具/设置）、主页卡片面板、每个工具页有渐变横幅 + 预览示意 + 三项特性说明，下方是带图标的设置卡片；快捷键显示为强调色键帽，点击录制（闪烁提示），× 清除；修改后底部浮出“未保存的更改”栏（保存/放弃，Ctrl+S），关闭时提醒未保存；操作结果用浮动提示代替底部文字。快速启动编辑器按任务类型只显示相关字段。
- 剪切板历史：WPF-UI 主题画刷，标题区、筛选胶囊、带类型图标的卡片，悬停才出现操作按钮，选中项强调色竖条。
- 搜索栏入场动画与选中竖条；监控条圆角阴影与淡入；托盘菜单 Win11 风格渲染（`TrayMenuRenderer.cs`）。
- `./scripts/test.ps1`：75 项通过，0 warning。新增检查：停靠右下角、弧线排列、新内容最靠近角落、保存栏显隐。
- 未完成：`-InteractivePaste` 本轮在“测试目标获得焦点”一步被环境阻断（前台被其他窗口占用），衣架真实点击粘贴未经自动验证，需人工验收。`publish.ps1` 失败：正在运行的 `dist/self-contained/AirTools.exe`（旧版）锁定文件，失败时已删除该目录下的 licenses/ 子目录；退出旧进程后重新运行 `./publish.ps1 -SelfContainedOnly` 即可恢复。

## 已验证

最终 `./scripts/test.ps1`：66 项通过，构建 0 warning / 0 error。

本轮较早的 `-InteractivePaste` 运行曾完整通过（当时共 71 项）；之后加了导航回归，交互重跑时目标窗口拿不到前台，两次失败。不能声称最终版本已完整通过 76 项交互测试。详见 `VALIDATION.md`。

覆盖：衣架模型、非激活窗口、自身剪切板写入抑制、历史导入备份、快捷键清空/冲突、搜索、任务退出码、索引、图片布局、设置浅色/深色、五页导航点击、清除按钮实际保存。较早交互运行确认 WM_PASTE / Shift+Insert 完整粘贴一次且目标保持前台；真实用户应用仍需验收。

## 产物与运行

- 自包含版：`dist/self-contained/AirTools.exe`
- 框架依赖版：`dist/framework-dependent/AirTools.exe`（需要 .NET 8 Desktop Runtime）
- 运行自包含版即可，不需要安装 .NET。
- 托盘菜单可打开设置、剪切板历史、晾衣架和监控。

## 仍需人工验收

- 在记事本、浏览器、VS Code、终端分别点击衣物，确认目标应用的输入语义。
- 多屏、不同 DPI、管理员权限窗口和长期运行。
- 开机重启后的自启；设置页现在能保存 HKCU Run。
- 图片/文件历史仍使用 WPF 剪切板 API，当前“晾衣架”只接文本。
- 跨平台与 Avalonia 尚未实现，当前技术是 WPF + Win32，适合 Windows 稳定性和原生焦点控制。

## 接下来 TODO

1. 优先在用户实际失败的输入应用复验点击粘贴；检查 `paste.log` 的控件类别、消息投递和 SendInput 结果。Windows 不能通过“API 返回成功”推断应用真的接受了文本，不要给所有 HWND 盲发 WM_PASTE 后宣称成功。
2. 在不被其他桌面操作干扰时重新运行交互测试。目标无法获得前台要明确标为环境阻断，不要混进成功统计。
3. 设置和历史窗口已使用 Fluent；系统监控配置、图片窗口、SSH 任务详情的旧布局尚未全面重做。沿用 WPF-UI 控件体系继续统一视觉，避免为了风格重写工具业务。
4. 多屏和不同 DPI 下验证拖动、屏幕边缘约束、透明区域穿透；再决定是否增加字号/衣服宽度设置。
5. 完善快捷键逐项冲突展示，当前汇总在设置底部，清空确实保存为空，不被其他冲突阻断。

## 仓库与关键文件

工作目录 `D:\Users\DELL\Desktop\windows-work\AirTools`，分支 `independent-rebuild`。工作区有此前实现及本轮未提交修改，不要 reset 或丢弃。旧 PowerToys 实验在 `codex/command-palette-extension` 和 stash `command-palette-experiment-before-independent-rebuild`。

- `src/AirTools/Services/AppHost.cs`：唯一监听、托盘、生命周期、快捷键及粘贴协调。
- `src/AirTools/Tools/Clipboard/RackWindow.cs`：透明衣物矢量、稳定控件、非激活交互和掉落动画。
- `src/AirTools/Tools/Clipboard/Services/PasteHelper.cs`、`NativeTextClipboard.cs`、`ClipboardManagerService.cs`：粘贴与自身写入抑制。
- `src/AirTools/LauncherSettingsWindow.cs`：Fluent 设置导航、录制和清除。
- `src/AirTools/Services/GlobalHotkeys.cs`：冲突回滚、空值和可用绑定注册。
- `tests/AirTools.Tests/Program.cs`、`RackTests.cs`：自动回归与原生/WPF 目标夹具。

给下一模型的简短提示词：先阅读本文和 VALIDATION.md，保留独立 exe 架构及现有未提交修改。用户最关注透明简洁的衣架、不抢焦点且真正粘贴、快捷键可清空保存、成熟 Win11 Fluent UI。先用用户实际应用与诊断日志查清粘贴问题；有证据后修复，发布到 dist/self-contained，并如实区分通过、跳过和环境阻断。

## 常用命令

```powershell
$env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'AirTools-Dev/dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
./scripts/test.ps1 -InteractivePaste
./publish.ps1 -SelfContainedOnly
```
