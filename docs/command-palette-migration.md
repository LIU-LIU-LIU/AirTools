# AirTools 接入 PowerToys Command Palette

调研日期：2026-10-27。独立版基线提交：`bac3d61`。本文件是迁移设计，尚未实现或安装扩展。

## 选择

采用微软推荐的 **Command Palette Extension**，不 fork PowerToys，不引用它的私有项目，不编译或替换 PowerToys 本体。

在同一 AirTools 仓库的 `codex/command-palette-extension` 分支开发，新增扩展项目和共享核心；独立版项目先保留，验证通过后再决定是否停止维护独立搜索入口。不另建仓库，避免配置、任务执行、测试代码各维护一份。

扩展通过官方 NuGet SDK 与 Toolkit、进程外 COM 和带包身份的 app manifest 接入。MSIX 安装/开发部署后由命令面板发现，不是把 DLL 复制到 PowerToys 目录。后续只构建和更新自己的包；PowerToys 更新后做兼容验证，需要时更新 SDK，无需逐次同步上游源码。

## 设置与入口的边界

| 需求 | 官方扩展接入方式 |
| --- | --- |
| 命令面板打开剪切板 | TopLevelCommands 提供独立列表页，使用实时搜索、分类/筛选、详情预览及上下文操作 |
| 命令面板打开快速任务 | 提供快速任务列表；常用任务可作为顶层命令，便于宿主搜索、固定和分配快捷键 |
| 扩展设置 | ICommandProvider.Settings / ICommandSettings.SettingsPage，出现在命令面板设置的扩展页 |
| 编辑任务 | SDK FormContent / ContentPage 表单，提供增删改复制与执行结果；凭据使用本地安全输入窗口，避免明文设置字段 |
| c / q 单键别名 | 交给命令面板的别名与激活方式配置，c 用于剪切板，q 用于任务；须处理内置剪切板 c 别名冲突 |
| 唤出快捷键、主题、半透明外观 | 由命令面板自己的设置管理，不再注册 AirTools 搜索热键或绘制搜索框 |
| PowerToys 主设置左侧新增 AirTools 模块 | 当前公开 Command Palette 扩展接口不提供此挂载点；不能承诺通过插件实现 |

此前截图中的「扩展 → 剪贴板历史记录」属于命令面板自己的设置体验。AirTools 可以使用同一宿主体验，但不向现有微软内置剪切板扩展插入代码。可在验证后由用户禁用重复的内置剪切板入口。

## 工程划分

- **AirTools.Core**：与 UI 无关的历史条目、搜索排序、置顶/删除/清空、任务模型、持久化、UTF-8 终端执行及 SSH 抓包会话。移除 MessageBox、Application.Current、BitmapSource 和 ObservableCollection 对核心逻辑的依赖，改为数据、事件、返回结果及可替换接口。
- **AirTools.Agent**：用户会话中的单实例后台，使用事件驱动剪切板监听、限额图片缓存、DPAPI 凭据与任务生命周期管理；不显示搜索窗口，不启动监控器，不添加第二个常驻托盘图标。大图缩放查看窗口按需打开，可先复用独立版修复过的图片窗口。
- **AirTools.CommandPalette**：使用与目标宿主匹配的官方模板，提供两个顶层列表页、任务表单、扩展设置和操作提示；通过当前用户限定的命名管道调用 Agent，扩展重载不会清空剪切板或中断抓包。
- **现有 AirTools.WPF 项目**：作为回退入口保留，迁移期间不与 Agent 同时采集/写同一数据目录。

Agent 与扩展放在同一发布包中，扩展按需唤起 Agent；持续记录历史需要用户同意的开机启动/登录启动能力。关闭命令面板、禁用扩展和停止剪切板记录应明确区分，提供「暂停记录」「退出后台」操作。不能假设扩展 COM 进程永远常驻。

管道使用当前用户访问限制、请求大小上限、明确操作和取消令牌；按条目 ID 查询与操作，图片缩略图按需传输。不要向宿主一次返回全部原图，不在日志或搜索结果中显示凭据。

## 必须先验证的技术边界

1. **宿主兼容性**：本机安装 PowerToys 0.100.2；对应 tag 的官方扩展模板目标为 .NET 10 / Windows SDK 26100，SDK 包版本为 `0.11.260520004`。本机目前仅安装 .NET 8 SDK。实施时从已安装宿主的「Create a new extension」生成模板，固定它给出的稳定依赖，不照抄 main 分支或盲目升级全局独立版项目。
2. **设置挂载**：最小扩展先完成发现、两个入口、一个设置字段、更新/卸载/重载验证，之后再迁移业务。
3. **回到原窗口粘贴**：0.100.2 内置 ClipboardHistory 的 PasteCommand 使用 `Microsoft.CmdPal.Common.Messages.HideWindowMessage` 及内部 ClipboardHelper，不是公开第三方扩展 API。第三方不能直接引用该实现。需验证公开 Dismiss/Hide 返回结果配合 Agent 捕获目标窗口、等待宿主隐藏、确认焦点和 SendInput 的完整链路。复制失败、权限不匹配或焦点不可确认时只保留复制结果并提示，不能往任意当前窗口粘贴。
4. **图片体验**：列表详情使用官方 Details / 图像内容；完整缩放/平移预览走自有按需窗口。宿主详情预览不等于提供现有图片查看器全部交互。
5. **历史来源**：不把当前历史替换成 Windows Win+V 历史。官方内置扩展使用系统历史，其采集与保留策略不同。AirTools 继续自己的文本/文件历史和置顶，图片维持本次 Agent 会话保留。

## 分阶段迁移与验收

1. 最小官方扩展：安装发现、两个列表入口、扩展设置、别名/快捷键，以及安装包更新与重载。先证明宿主集成；不改旧历史、不自动开始任务。
2. 快速任务：复用模型/执行器，添加宿主任务列表与表单；测试中文输出、真实退出码、工作目录、独立参数、复制任务、密码保存、停止抓包与日志。导入当前 launcher.json 前备份，旧版保持可恢复。
3. 剪切板：抽离 Agent，迁移历史与操作，验证记录、分类、即时搜索、置顶、清空非置顶、文件/图片复制、详情及大图预览。使用主机隐藏后的可靠焦点检查完成自动粘贴。
4. 切换：验证重载、重启、休眠恢复、Agent 单实例、并发写入、目标窗口关闭、管理员窗口、中文输入法和不同缩放屏幕。达到功能与性能基线后再停止旧版热键与托盘启动。

只迁移剪切板和快速任务。文件搜索、全局搜索 UI、主题/快捷键管理和系统监控不进入扩展；系统级入口能力交给 PowerToys。

## 官方依据

- [扩展模型](https://learn.microsoft.com/windows/powertoys/command-palette/extensibility-overview)：独立进程、WinRT API、manifest 发现、页面种类及扩展设置。
- [官方创建与部署方式](https://learn.microsoft.com/windows/powertoys/command-palette/creating-an-extension)：从命令面板生成模板、Deploy 包、Reload；普通 build 不等于部署。
- [命令面板设置](https://learn.microsoft.com/windows/powertoys/command-palette/settings)：扩展、别名和快捷键由宿主管理。
- [0.100.2 对应模板](https://github.com/microsoft/PowerToys/tree/v0.100.2/src/modules/cmdpal/ExtensionTemplate/TemplateCmdPalExtension)：以本机版本为兼容基线。
- [SDK/Toolkit](https://github.com/microsoft/PowerToys/tree/v0.100.2/src/modules/cmdpal/extensionsdk)：CommandProvider.Settings、SettingsPage、ListPage、FormContent 等公开适配点。
- [内置剪切板粘贴实现](https://github.com/microsoft/PowerToys/blob/v0.100.2/src/modules/cmdpal/ext/Microsoft.CmdPal.Ext.ClipboardHistory/Commands/PasteCommand.cs)：当前使用宿主内部消息，不能当作外部插件可直接复用的接口。
- [PowerToys MIT 许可证](https://github.com/microsoft/PowerToys/blob/main/LICENSE)：若复用具体代码须保留相应版权/许可声明；本设计优先引用公开 NuGet 接口。
