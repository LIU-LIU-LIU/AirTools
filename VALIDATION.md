# 独立版验证 · 2026-11-17

分支：`independent-rebuild`。Windows 首版使用 .NET 8 WPF + Win32；单个 AirTools 进程，不需要旧插件或 Agent。旧实验保留在原分支和 stash `command-palette-experiment-before-independent-rebuild`。

## 已执行

- Release 构建：0 warning、0 error。
- 最终 `scripts/test.ps1`：66 项检查通过。包含衣架模型与非激活窗口、自身剪切板写入抑制、旧历史导入备份、快捷键清空与冲突、任务 UTF-8 和退出码、索引、图片布局、搜索、Fluent 设置页浅色 / 深色渲染、五个导航项点击切换、清除按钮实际保存空快捷键、Fluent 历史列表。
- 本轮较早的交互运行共 71 项通过，向独立原生编辑器和 WPF 输入框实际点击衣服，分别确认 `WM_PASTE` / `Shift+Insert` 完整粘贴一次、原窗口保持前台。随后两次重跑因测试目标无法获得前台而失败，不能算作最终交互复验通过；最终常规回归未包含这两项交互测试。
- WPF RenderTargetBitmap 渲染晾衣架并检查，调整了衣服挂点对齐、正文边距及省略显示。
- 启动开发文件，用 Computer Use 检查设置页面；发现最初导航选择事件没有触发，修复为直接响应导航项点击，并增加五页点击回归。再次 UI 检查受到用户输入打断，未宣称最新真实点击复验完成。
- 自包含发布成功：`dist/self-contained/AirTools.exe`，约 68.1 MiB；附带 LICENSE、NOTICE、依赖许可文本。无需安装 .NET Runtime。
- `git diff --check` 通过。

## 仍需人工验收

自动测试已在临时原生编辑器和 WPF 输入框中通过真实鼠标点击，分别验证 `WM_PASTE` 与 `Shift+Insert`，并确认原目标窗口保持前台。仍需在记事本、浏览器、VS Code、终端、多屏不同 DPI 和管理员窗口中手工确认。图片 / 文件历史继续使用 WPF 剪切板 API；晾衣架当前只接文本。

## 快速验收

1. 退出旧插件后台后打开 `dist/self-contained/AirTools.exe`，让光标停在测试编辑器内。
2. 连续复制 6 段文本，确认衣架只保留最近 5 条；同一文本再次复制应移到第一件。
3. 点击任一衣服，确认原输入窗口仍有焦点，收到完整文本且只出现一次。
4. 点击夹子，确认掉落动画且完整历史仍保留；快捷键 Ctrl+Alt+1 粘贴最新一件。
5. 托盘打开设置，录制热键、改为 3 件、设置自启，保存后确认生效。开机自启由 HKCU Run 注册，仅指向当前独立程序路径。
