# Release 验证记录（2026-10-26）

## 本轮修复

- 默认搜索快捷键 Ctrl+Space，输入栏内显示 c/f 模式提示。
- 搜索栏独立模板移除 WPF UI 默认输入下划线；柔和背景、20 DIP 外圆角、14 DIP 搜索栏圆角、圆角候选项，保留原生 Acrylic。
- 标题使用 Air Tools，程序与托盘使用原版蓝底白框图标。
- Python 与 Windows PowerShell 原生管道统一 UTF-8；stderr 不再直接判定失败，使用实际退出码。测试同时覆盖中文 stdout/stderr 和退出码 0/7。
- 设置窗口补充原生标题栏；首次显示延迟至消息循环就绪，并再次请求输入焦点。

## 自动检查

运行：

```powershell
dotnet build tests/AirTools.Tests -p:OutputPath="$env:TEMP/AirTools-tests-build/"
& "$env:TEMP/AirTools-tests-build/AirTools.Tests.exe"
```

38 项检查通过。包含命令参数转义、DPAPI、真实 PowerShell/Python 子进程、快捷键冲突回滚、历史兼容、复制失败终止粘贴、目录增量索引、三组大图/长图/DPI 布局、空词候选、过期结果取消、设置创建和输入栏提示。

测试使用独立临时数据目录。图片布局通过 WPF 实际布局检查，搜索窗口通过 RenderTargetBitmap 渲染并人工检查；尚未完成多屏不同显示缩放和人工自动粘贴全链路验证。

## 性能抽样

Release 框架依赖发布版，监控器未启动，启动搜索窗口后的单次五秒抽样：工作集 163.8 MB，私有内存 125.3 MB，平均 CPU 0.31%（进程 CPU 时间 / 墙钟时间）。

这不是长期基准：工作集尚未达到原计划 120 MB 目标；未测 P95 唤出延迟。不可把单次结果当作内存泄漏或已满足全部性能指标的证据。

## 尚需真实环境验证

- Python ADB 抓包脚本本身涉及设备和 Wireshark：本轮仅用无副作用 Python fixture 验证编码及退出码，未自动开始设备抓包。
- SSH 抓包需实际服务器、凭据、主机指纹确认和 tcpdump 权限；尚未做真实抓包验证。
- Ctrl+Space 在部分输入法/编辑器中也有用途；可在设置中录制其他快捷键，注册冲突会提示。

## 使用

`dist/framework-dependent/AirTools.exe` 为 Release 单文件版本，需要 .NET 8 Desktop Runtime；`dist/self-contained/AirTools.exe` 包含运行时。

启动进入托盘；带 `--search` 打开搜索框，`--settings` 打开设置。配置位于 `%LOCALAPPDATA%/AirTools/launcher.json`，日志位于其 `Logs` 子目录。密码为当前 Windows 用户 DPAPI 密文，不应跨用户复制。

终端任务的 Close / KeepOnFailure / Keep 分别为结束关闭、失败保留、始终保留。原始参数每行一个，无需包裹引号；复杂脚本使用 PowerShell 类型。

此前临时版中编辑过的任务已迁移到正式数据目录；迁移来源另存为 `launcher.import-backup.json`，原剪切板历史保持原位置。

## 产品方向

PowerToys 使用 MIT 许可证，允许按许可证要求二次开发。官方 Command Palette 支持扩展：

- https://github.com/microsoft/PowerToys
- https://github.com/microsoft/PowerToys/blob/main/LICENSE
- https://learn.microsoft.com/windows/powertoys/command-palette/extensibility-overview

建议把 PowerToys 当作通用系统工具使用，AirTools 聚焦剪切板与个人抓包/脚本任务。若以后只需要一个入口，优先将独立剪切板服务包装成 Command Palette 扩展；维护整个 PowerToys 分支会增加构建、更新和兼容成本，本轮未引入该依赖。
