## Why

网关页右侧“模型组合”面板当前仍会渲染已经软删除、处于“不存在”状态的 Combo，造成用户误以为这些历史数据仍可编辑。软删除数据需要继续保留，以便后续同标识组合恢复时复用，但不应占用右侧编辑面板。

## What Changes

- 右侧模型组合编辑面板不再展示 `IsDeleted` 的 Combo。
- 左侧 Endpoint 的 flags 组合下拉框继续展示已绑定但不存在的 Combo，并保留“不存在”状态提示与交互能力。
- 增加 UI 契约测试，锁定左右两处的差异化展示规则。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `gateway-panel-ui`: 明确不存在的 Combo 在右侧编辑面板隐藏、在左侧 flags 组合下拉框保留的展示规则。

## Impact

- 影响 `LoomX/Views/GatewayView.axaml` 的右侧 Combo 项可见性。
- 影响 `LoomX.Tests/Views/GatewayViewContractTests.cs` 的网关页面 UI 契约测试。
- 不修改数据库、配置 API、软删除数据或 Endpoint 绑定逻辑。