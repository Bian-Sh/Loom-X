# Gateway Endpoint 列表选中交互设计

## 背景

Gateway 页面左侧的 Endpoint 列表当前仅通过 `AccentSoftBrush` 背景表示选中项。Provider 页面供应商列表已经形成了更清晰的选中交互：保留列表底色、使用左侧 3px Accent 指示当前项，并为选中项及其悬停状态显式覆盖 `ListBoxItem` 模板内容的背景和前景。

## 目标

- 让 Gateway 左侧 Endpoint 列表的选中体验与 Provider 页面供应商列表一致。
- 保留现有 Endpoint 的选择绑定、行布局、启用开关、复制 URL 和右侧编辑内容。
- 通过契约测试锁定选中态和悬停态样式，避免后续主题调整时回退。

## 方案

仅在 `GatewayView.axaml` 的 `endpoint-list` 局部样式中补齐 Provider 列表已有的选中交互规则：

1. 选中项使用 `SurfaceSubtleBrush` 背景、`AccentBrush` 左侧 3px 边框。
2. 选中项的 `ContentPresenter#PART_ContentPresenter` 使用相同背景和 `TextPrimaryBrush` 前景，避免 Avalonia 默认选中模板覆盖行内容。
3. 选中项悬停时，`ListBoxItem` 及其 `ContentPresenter` 使用 `SurfaceMutedBrush`，前景保持 `TextPrimaryBrush`。
4. 维持现有 `SelectedItem="{Binding SelectedEndpoint}"` 绑定和 Endpoint 行内部控件不变。

不抽取全局共享样式，避免扩大影响范围；不修改右侧 Combo、路由成员或模型选择列表。

## 测试

在 `GatewayViewContractTests` 的 Endpoint 列表契约中增加以下断言：

- 选中项背景、左侧 Accent 边框和 3px 边框厚度。
- 选中项 ContentPresenter 的背景和前景。
- 选中悬停项及其 ContentPresenter 的背景和前景。
- 现有列表背景、行间距和底部分隔线契约继续保留。

验证命令：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~GatewayViewContractTests
```
