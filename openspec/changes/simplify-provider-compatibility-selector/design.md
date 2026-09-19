## Context

兼容类型已由 `ProviderCompatibilityOption` 集中映射到 `ApiMode` 与 `EndpointFormat`，基础 Tab 目前通过三张 RadioButton 卡片绑定同一 `SelectedCompatibility` 属性。现有本地化资源分别提供类型标题和 URI 描述。

## Goals / Non-Goals

**Goals:**

- 复用现有兼容类型集合和双向选择属性，以更紧凑的控件呈现。
- 保持类型标题和 URI 描述的本地化能力。

**Non-Goals:**

- 不改变兼容类型集合、字段映射、自动保存或请求路由。
- 不调整基础 Tab 的其他字段。

## Decisions

- 使用绑定 `CompatibilityOptions` 与 `SelectedCompatibility` 的 `ComboBox` 替代三张单选卡片，避免新增事件处理和状态。
- 下拉项和外部辅助文本直接复用现有本地化资源，并通过兼容类型匹配转换器控制可见性，避免新增显示状态或事件处理。
- 视图契约测试固定下拉绑定、URI 外置和旧卡片移除，防止重新引入占位较大的布局。

## Risks / Trade-offs

- [下拉模板重复三个固定选项的显示分支] → 兼容类型目前只有三个固定值，保持显式分支可直接复用响应式本地化绑定，避免引入额外状态。
- [下拉选择导致映射行为变化] → 继续绑定既有 `SelectedCompatibility`，并保留现有映射单元测试。
