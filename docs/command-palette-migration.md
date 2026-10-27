# Command Palette 接入说明

本分支已迁移到官方进程外 COM/WinRT 扩展模型。通过 MSIX 中 com.microsoft.commandpalette 清单注册，不引用私有 PowerToys 项目或覆盖宿主文件。

Core 提供模型、原子配置、导入、搜索和 IPC。Agent 管理剪切板、会话图片、DPAPI、终端及 SSH 抓包；按需打开图片/凭据/主机指纹窗口。CommandPalette 主页只注册剪切板与快速任务两个入口。扩展自定义设置页承载任务 CRUD、剪切板选项和关于信息；任务不会注册为主页命令。重载扩展不会自动清空 Agent 图片或结束任务。

宿主拥有搜索框、激活键、外观及别名；公开 API 不提供自定义 PowerToys 主导航模块或原窗口 HWND。自动粘贴通过 Agent 记录前台目标并严格验证焦点；无法验证则仅保留复制。单键别名可能与内置扩展冲突。

main 保留独立版完整源码，基线 bac3d61。本分支只保留插件及后台，数据目录独立为 AirTools.CommandPalette，首次从旧目录备份导入，不修改旧数据。

兼容基线为本机 Command Palette 0.11.11762.0 自带模板：.NET 10、Extensions SDK 0.9.260303001；Agent 使用 .NET 8。版本固定在项目中，后续更新只需维护自己的包并验证宿主兼容性。

本地开发注册无需商店，但需要 Windows 开发者/旁加载策略。本机用户已启用开发者模式，注册成功。正式上架需替换开发身份 CN=AirTools 为 Partner Center 实际身份；也可按官方文档通过 WinGet 分发。升级前停止 Agent，更新包并重载；包外用户数据保留。

详见 README、validation.md 和微软官方文档：

- https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/extensibility-overview
- https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/publish-extension
- https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/publish-extension-store
