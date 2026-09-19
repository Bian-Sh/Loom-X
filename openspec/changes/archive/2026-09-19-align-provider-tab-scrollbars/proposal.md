## Why

Provider 右侧详情面板四个 Tab 的主纵向滚动条需要位于用户截图标出的红色参考线，同时表单内容区域右边缘必须保持原位。此前通过修改外层 Margin 或把模板 ScrollBar 平移出父级边界，分别造成内容跟随移动或潜在裁剪/遮盖问题。

## What Changes

- 四个 Tab 的外层 `ScrollViewer` 保持完整宽度，使原生纵向 ScrollBar 留在模板自身层级和目标位置。
- 通过 `ScrollViewer.Padding="0,0,8,0"` 只把 ScrollContent 向左保留 8px，不移动 ScrollBar。
- 保持左侧 Provider 目录和测试响应文本框的嵌套滚动条不变。
- 修正基础 Tab 的 API Key 输入框，使其右边缘与显示名称、Base URL 输入框一致，同时保留眼睛按钮覆盖布局。
- 更新契约测试，锁定“原生滚动条层级 + 仅内容内缩”的布局。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `provider-panel`: 右侧详情 Tab 的内容边界保持原位，主纵向滚动条在原生模板层级对齐用户指定参考线。

## Impact

- 修改 `LoomX/Views/ProvidersView.axaml`。
- 修改 `LoomX.Tests/Views/ProvidersViewContractTests.cs`。
- 不涉及数据库、公开 API、运行时配置或依赖变更。
