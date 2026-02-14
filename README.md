# Air Tools

Windows 桌面工具集，**单 exe 单进程**，一个托盘图标，一个开机自启注册表项。

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
