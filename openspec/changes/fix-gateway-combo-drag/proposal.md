## Why

网关页右侧 Combo 的成员模型 cell 当前无法稳定启动拖拽，用户无法调整故障转移顺序。问题出现在拖拽源复用可点击 `Button` 的指针按下事件，按钮自身的指针捕获会干扰 Avalonia 的拖放手势。

## What Changes

- 将成员模型拖拽源改为不参与按钮点击捕获的 cell 抓手容器，确保按下并移动时能够进入 Avalonia 拖放流程。
- 保留现有拖拽目标、路由重排、重编号和持久化逻辑。
- 增加视图契约回归测试，固定拖拽源不会再使用可点击按钮并仍绑定拖放处理。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

无。此次仅修复既有实现，不改变规范级行为。

## Impact

- `OllamaHub.Desktop/Views/GatewayView.axaml`
- `OllamaHub.Desktop/Views/GatewayView.axaml.cs`（如需调整事件入口）
- `OllamaHub.Tests/Views/GatewayViewContractTests.cs`
- 不涉及服务端 API、数据库结构或运行时配置。
