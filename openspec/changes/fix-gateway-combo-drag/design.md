## 修复方案

将成员模型行左侧抓手从 `Button` 替换为带 `PointerPressed` 的 `Border`（或等价非按钮输入容器），继续通过 `Tag` 携带 `GatewayRouteEditorViewModel`。非交互容器不会先执行按钮的按下/捕获逻辑，事件处理器可直接调用现有 `DragDrop.DoDragDropAsync`。成员行外层 `Border` 继续接收 `DragOver` 和 `Drop`，ViewModel 的 `MoveRouteAsync` 不变。

## 数据流

用户按下抓手 → 视图读取路由 ID 并创建 `DataTransfer` → Avalonia 执行移动拖放 → 目标成员行校验 GUID 并调用 `MoveRouteAsync` → 路由集合移动、重编号、逐条保存。

## 验证

在现有视图契约测试中断言抓手是非 `Button` 输入容器，同时保留 `PointerPressed`、`DragOver`、`Drop` 绑定；运行全部 `OllamaHub.Tests` 和桌面项目构建，确认 XAML 编译及现有持久化链路不受影响。
