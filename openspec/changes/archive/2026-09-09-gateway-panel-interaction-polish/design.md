## 实现设计

左侧 Endpoint 集合由 `ListBox` 改为放在 `ScrollViewer` 内的 `ItemsControl`。这样每个 Endpoint 仍按原模板显示，并保留模板中的明确按钮、开关、弹出选择器和下拉框，但容器不再产生选中、悬浮或整行点击语义。删除旧 `endpoint-list ListBoxItem` 样式，保留列表表面和分隔线。

主工作区采用 `3*,2*` 两列，形成稳定的 60/40 比例。右侧 Combo 与成员删除按钮仅替换图标路径，直接复用 `ProvidersView` 中供应商 cell 的删除 `Path` 数据和 `icon-glyph` 主题样式，不新增资源。

API Key 刷新按钮删除悬浮可见规则，改为默认常驻；图标由 `PathIcon` 改为带 `icon-glyph` 类的 `Path`，显式使用共享图标前景语义。契约测试通过 XAML 结构断言固定这些要求。

## 验证

- 运行 `GatewayViewContractTests`。
- 运行完整测试与解决方案构建。
- 重新发布到 `outputs/<可读时间>/`。
- 启动发布包并通过桌面 UI 检查网关页面布局、无 cell 选中/悬浮反馈、删除图标和刷新按钮呈现。
