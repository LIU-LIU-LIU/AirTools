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

## 构建

```bash
cd AirTools
dotnet build AirTools.sln
```

发布单文件：
```bash
dotnet publish src/AirTools/AirTools.csproj -c Release
```

输出：`src/AirTools/bin/Release/net8.0-windows/win-x64/publish/AirTools.exe`
