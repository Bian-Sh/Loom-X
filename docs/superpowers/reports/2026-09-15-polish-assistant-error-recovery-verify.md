# 助手异常气泡与有限自动重试验证

## 验证范围

本次 change：`polish-assistant-error-recovery`。正文色 `#303639` 仅用于首版效果预览，不代表用户已确认最终配色。

## 完整性

- 6 项任务全部完成；新增 `assistant-error-recovery` 规格通过严格 OpenSpec 校验。
- 删除手动重试、忽略命令和独立失败卡片；终态失败使用消息流 `Error` 条目。
- 异常气泡为左对齐、640px 最大宽度、14px 普通字重、22px 行高、淡红透明表面及细描边。
- 异常表面的 Alpha 最低为 217/255（约 85%），透明关闭后为完全不透明。

## 正确性与验证证据

1. 先运行新回归测试，确认旧实现出现 8 项预期失败：单次请求而非五次尝试、状态条目而非错误条目，以及缺少异常表面。气泡 XAML 契约也在旧实现上预期失败。
2. `AgentLoopRetryTests` 覆盖首次计入五次上限、第五次成功、确定性错误只请求一次、额度不足的 429 不重试、底层连接重置、HTTP 状态兜底、输出后不重放、超时、指数退避、服务端等待提示和取消等待。
3. 新日志测试确认用户输入、上游详情、Authorization、自定义 Header 和密钥样例不会进入重试日志。
4. ViewModel 测试确认历史失败事件保留原时间及正文，下一轮不会清除错误条目，也不会把异常消息作为 Markdown 回答。
5. Avalonia 实际控件测试确认气泡的字号、字重、行高、换行、背景和布局，并确认无操作按钮。
6. `dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore`：652 项通过，0 失败。
7. `dotnet build LoomX.slnx --configuration Release --no-restore --nologo`：0 错误。
8. `dotnet test LoomX.Tests/LoomX.Tests.csproj --configuration Release --no-restore`：652 项通过，0 失败。
9. `openspec validate polish-assistant-error-recovery --strict` 与 `git diff --check`：通过。

## 视觉验证及发布

- 使用真实 `AssistantView`、应用视觉资源和 `ChatMessageViewModel.Error`，在独立 Avalonia 预览进程中渲染浅色、深色、灰色及渐变着色背景；未接入真实用户数据库或真实会话。
- 通过 CUA 后台读取预览窗口及截图，确认错误正文完整显示且没有重试或忽略操作。仅截取该预览窗口，没有截取桌面。
- 这些图片是模拟着色背景上的真实控件渲染，并不是系统 Acrylic 的真实桌面色彩采样；实际系统材质仍需用户使用发布包查看。截图中的错误内容为模拟数据，不表示系统会自动追加该文字。
- 预览目录：`outputs/20260915-异常气泡预览/`。
- 临时预览程序：`.local/assistant-error-preview-20260915/`，仅为本次任务创建并保留，不纳入业务部署。
- 发布命令：`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-desktop.ps1`。
- 自包含 Windows 发布目录：`outputs/20260915-170756/`，可执行文件为 `LoomX.exe`；发布成功。

## 一致性及已知限制

- 重试在同一 Agent 模型步骤内执行，不重复用户消息或工具执行；每次尝试独立计时，最多四次重试等待。
- `Retry-After` 自动等待上限为 60 秒，没有合法提示时等待 1、2、4、8 秒；用户取消始终可打断。
- 已经收到流式事件后即使遇到瞬时错误也不重放，避免重复正文与潜在工具行为。
- 本次只修正异常消息的阅读层；未改动标题、普通状态、其他页面等透明区域的配色。
- 原有构建警告仍存在：SQLitePCLRaw 依赖漏洞警告、SettingsViewModel 空值初始化警告及其他现有分析器警告。未在本次变更中升级依赖或修改无关模块。
- Git 同步时自动维护提示坏 tree 对象 `e42a7d13307188ed6a5459b2a5c6ce4d1d47930d`；未进行 reset、clean、stash 或对象清理。
- 原有 `incremental-config-edit` 轨迹改动及 `.zcode/` 未跟踪文件原样保留，不纳入本次提交。

## 结论

实现和自动化验证通过，首版视觉效果及发布包已交付。正文最终颜色与真实透明材质视觉效果等待用户评审，本次不自动归档。
