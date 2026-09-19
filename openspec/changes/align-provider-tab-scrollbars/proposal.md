## Why

Provider 右侧详情面板四个 Tab 的主纵向滚动条需要统一对齐到用户截图标出的红色参考线。先前额外增加的 8px 页面补偿使滚动条过度向左，未落在目标位置。

## What Changes

- 为 Provider 右侧详情面板四个 Tab 的外层 `ScrollViewer` 保留统一样式标记。
- 移除额外的 8px 右侧 Margin，让主纵向滚动条使用 `TabControl` 模板自身的右侧内缩并对齐截图参考线。
- 保持左侧 Provider 目录和测试响应文本框的嵌套滚动条不变。
- 更新视图契约测试，防止各 Tab 的滚动条定位再次分化。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `provider-panel`: 增加右侧详情 Tab 主纵向滚动条统一对齐用户指定参考线的布局要求。

## Impact

- 修改 `LoomX/Views/ProvidersView.axaml`。
- 修改 `LoomX.Tests/Views/ProvidersViewContractTests.cs`。
- 不涉及数据库、公开 API、运行时配置或依赖变更。
