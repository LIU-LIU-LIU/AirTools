# Air Tools

Windows 桌面工具集，**单 exe 单进程**，一个托盘图标，一个开机自启注册表项。

新版以搜索框为主入口：默认 **Ctrl+Space** 唤出，空输入时按 **c** 搜索剪切板，按 **f** 搜索常用目录。上下键选择，Enter 执行；剪切板模式 Enter 复制并粘贴，Ctrl+Enter 仅复制，Ctrl+P 预览图片。

托盘的「设置与快速启动」可编辑程序、终端、PowerShell 和 SSH 抓包任务。程序参数每行一个，终端中文输出统一 UTF-8。系统监控本轮保持现状。

发布版使用 `dist/framework-dependent/AirTools.exe`（需要 .NET 8 Desktop Runtime）或 `dist/self-contained/AirTools.exe`（自带运行时）。执行 `--settings` 打开设置，`--search` 打开搜索框。验证结果和已知限制见 [VALIDATION.md](VALIDATION.md)。

## 预览

<img width="360" height="480" alt="PixPin_2026-02-14_16-07-46" src="https://github.com/user-attachments/assets/12f9d1e8-cb9d-48de-a17c-4d2dd782d097" />

<img width="360" height="508" alt="PixPin_2026-02-14_16-08-20" src="https://github.com/user-attachments/assets/7d48e50b-8bc4-46bf-952e-a2ab459445d1" />

<img width="480" height="640" alt="PixPin_2026-02-14_16-09-27" src="https://github.com/user-attachments/assets/88a29321-4ee7-41ad-a2d2-5ce5cc987985" />


## 架构

```
AirTools.exe（唯一进程）
├── 主窗口：工具列表
├── 剪切板管理器窗口
├── 系统资源监控窗口
└── 托盘图标（一个）
```

## 目录结构

```
AirTools/
├── AirTools.sln
└── src/
    └── AirTools/
        ├── App.xaml
        ├── MainWindow.xaml           # 工具列表
        ├── SettingsWindow.xaml       # 开机自启设置
        ├── Services/
        │   └── StartupService.cs
        └── Tools/
            ├── Clipboard/            # 剪切板模块
            │   ├── ClipboardWindow.xaml
            │   ├── ClipboardSettingsWindow.xaml
            │   ├── ImageViewerWindow.xaml
            │   └── Services/
            └── SystemMonitor/        # 系统监控模块
                ├── MonitorWindow.xaml
                └── MonitorSettingsWindow.xaml
```

## 功能

- **剪切板管理器**：历史记录、置顶、搜索、快捷键唤出（默认 Ctrl+Shift+V）
- **系统资源监控**：CPU、内存、磁盘、网络，可配置刷新间隔、位置、透明度
- **开机自启**：仅注册 AirTools，所有工具随主程序启动

## 构建与发布

### 构建输出位置

- **Debug**：`src/AirTools/bin/Debug/net8.0-windows/AirTools.exe`
- **Release**：`src/AirTools/bin/Release/net8.0-windows/AirTools.exe`

### 本地构建

```powershell
cd AirTools
dotnet build AirTools.sln
```

### 发布版本（统一输出到 dist/）

所有发布产物统一输出到 `dist/` 目录：

```
dist/
├── self-contained/      # 自包含版 ~63MB，单 exe 即可运行
│   └── AirTools.exe
└── framework-dependent/ # 框架依赖版 ~250KB，需安装 .NET 8 桌面运行时
    └── AirTools.exe
```

### 一键发布（推荐）

```powershell
cd AirTools
.\publish.ps1
```

可选：`.\publish.ps1 -SelfContainedOnly` 或 `.\publish.ps1 -FrameworkDependentOnly` 只发布其中一个版本。

### 手动发布

```powershell
cd AirTools

# 自包含版
dotnet publish src/AirTools/AirTools.csproj -c Release -o dist/self-contained

# 框架依赖版
dotnet publish src/AirTools/AirTools.csproj -c Release -p:SelfContained=false -p:RuntimeIdentifier=win-x64 -p:PublishSingleFile=true -o dist/framework-dependent
```

> **PDB 文件**：调试符号，用于崩溃堆栈。分发给用户时可删除。

### 版本对比

| 版本       | 体积      | 依赖                     |
|------------|-----------|--------------------------|
| 自包含     | ~63 MB    | 无，单 exe 即可运行      |
| 框架依赖   | ~250 KB   | 需安装 .NET 8 桌面运行时 |
