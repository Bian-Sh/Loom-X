## Why

Provider 右侧详情面板的各个 Tab 目前缺少统一的滚动条横向定位约束，滚动条紧贴面板右边缘，视觉留白不一致。需要将四个 Tab 的主纵向滚动条统一向左收进 20px。

## What Changes

- 为 Provider 右侧详情面板四个 Tab 的外层 `ScrollViewer` 增加统一样式标记。
- 将这些主纵向滚动条统一定位到面板右边缘向左 20px。
- 保持左侧 Provider 目录和测试响应文本框的嵌套滚动条不变。
- 增加视图契约测试，防止各 Tab 的滚动条定位再次分化。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `provider-panel`: 增加右侧详情 Tab 主纵向滚动条统一向内偏移 20px 的布局要求。

## Impact

- 修改 `LoomX/Views/ProvidersView.axaml`。
- 修改 `LoomX.Tests/Views/ProvidersViewContractTests.cs`。
- 不涉及数据库、公开 API、运行时配置或依赖变更。
