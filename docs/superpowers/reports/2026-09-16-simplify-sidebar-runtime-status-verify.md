# 侧栏版本与运行状态验证报告

- 变更：`simplify-sidebar-runtime-status`
- 日期：2026-09-16
- 结论：通过

## 完整性

- `tasks.md`：3/3 项已完成。
- 增量规格“侧栏底部以紧凑方式展示版本与运行状态”已映射到主窗口 XAML、主窗口 ViewModel 和契约测试。
- 发布产物已生成于 `outputs/20260916-183826/`，目录中唯一应用入口为 `LoomX.exe`。

## 正确性

- 版本号绑定 `AppVersion.Label`，不再使用静态版本标签。
- 网关状态映射完整：运行中使用成功色，启动/停止过程使用警告色，失败使用错误色，未运行使用中性色。
- `GatewayProcessService.StateChanged` 会刷新状态提示及全部互斥状态属性，`Dispose` 会解除订阅。
- 圆点区域提供本地化 ToolTip 和自动化名称，颜色不是唯一状态信息。
- 契约测试覆盖紧凑布局、四类状态绑定、版本来源和事件订阅释放。

## 一致性

- 实现遵循 `design.md`：未新增转换器、视觉 token、状态模型或网关生命周期逻辑。
- 复用现有 `overview.gateway.status.*` 本地化资源和现有主题画刷。
- 改动集中于主窗口、主窗口 ViewModel 和对应契约测试，未触及数据库、API 或依赖。

## 验证证据

- `openspec validate simplify-sidebar-runtime-status`：通过。
- `dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore`：669/669 通过。
- `dotnet build LoomX.slnx --configuration Release --no-restore`：构建成功，0 错误。
- `pwsh -NoProfile -File scripts/publish-desktop.ps1 -Configuration Release`：发布成功，仅包含 `LoomX.exe` 应用入口。

## 已知非阻塞项

- 构建仍报告项目既有的 NuGet 漏洞提示及编译分析警告，本次改动未新增这些警告。
- 系统当前已有另一份 LoomX 实例运行；为避免单实例激活干扰用户窗口，本次未启动新发布包做 CUA 截图，使用 XAML 编译、契约测试和完整构建作为 UI 结构验证。