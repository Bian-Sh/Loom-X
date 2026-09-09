## Why

网关页面左侧 Endpoint 子项当前由 `ListBoxItem` 提供整行选中与悬浮反馈，视觉上像需要点击切换的 toggle cell，容易与子项内部的明确控件混淆。页面左右空间分配、删除图标和 API Key 刷新按钮的呈现也与现有 Provider 页面不一致。

## What Changes

- 取消左侧 Endpoint 子项容器的整行选中、悬浮和点击交互，只保留子项内部的启用、复制、Combo 选择和 Reasoning 控件。
- 将网关页面主工作区调整为左侧约 60%、右侧约 40%，使 Endpoint 配置获得更充足的横向空间。
- 右侧 Combo 和成员删除按钮复用 Provider 供应商列表 cell 右下角的删除图标。
- API Key 刷新按钮常驻显示，并使用主题前景色渲染，避免出现纯黑图标。

## Capabilities

### New Capabilities

- `gateway-panel-ui`: 网关页面 Endpoint 面板的展示与控件视觉契约。

### Modified Capabilities

无。该 UI 契约此前尚未纳入 OpenSpec 主规格。

## Impact

- `LoomX/Views/GatewayView.axaml`
- `LoomX.Tests/Views/GatewayViewContractTests.cs`
- 不修改 ViewModel、配置数据库、HTTP API 或网关运行时行为。
