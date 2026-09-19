## Why

Provider 基础 Tab 当前使用三张兼容类型卡片，重复展示类型名称与接口 URI，占用较多纵向和横向空间。将选择入口收敛为下拉菜单，并把当前接口 URI 作为单行辅助信息展示，可在不改变配置语义的前提下提升紧凑度。

## What Changes

- 将基础 Tab 的三张接口兼容类型单选卡片替换为单个下拉菜单。
- 下拉项仅显示兼容类型名称，不再在每个选项内重复展示接口 URI。
- 在下拉菜单外单独显示当前所选类型对应的接口 URI。
- 保持现有 `SelectedCompatibility` 映射、自动保存和持久化字段不变。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `provider-panel`: 调整基础 Tab 中接口兼容类型选择器的紧凑布局与辅助信息展示。

## Impact

- 影响 `ProvidersView.axaml`、兼容类型显示属性及 Provider 页面视图契约测试。
- 不修改数据库结构、Provider 配置字段、运行时请求协议或公共 API。
