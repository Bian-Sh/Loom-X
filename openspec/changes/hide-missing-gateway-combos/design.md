## Context

右侧模型组合面板与左侧 Endpoint flags 下拉框使用不同的数据呈现入口：右侧直接绑定全局 Combo 编辑集合，左侧绑定 Endpoint 的 `ComboOptions`。软删除 Combo 会设置 `IsDeleted`，数据与 Endpoint 绑定仍由现有配置链路保留。

## Goals / Non-Goals

**Goals:**
- 仅在右侧编辑面板隐藏 `IsDeleted` 的 Combo。
- 保持左侧 flags 下拉框对不存在绑定的可见性和交互能力。

**Non-Goals:**
- 不物理删除 Combo 或 Endpoint 绑定。
- 不改变 Combo 恢复、保存、删除或配置刷新逻辑。
- 不调整“不存在”状态的本地化文案和样式。

## Decisions

### 在右侧 Combo 项根容器应用可见性过滤

右侧项模板的根容器根据 `IsDeleted` 控制 `IsVisible`。这样软删除对象仍留在 ViewModel 集合和数据层中，但不会参与右侧布局或交互。

备选方案是在 `GatewayViewModel` 刷新时过滤集合。该方案会让右侧编辑集合与完整 Combo 数据失去一一对应，并需要额外处理删除后的即时移除、名称去重和 Endpoint 绑定同步，改动范围更大，因此不采用。

### 左侧 flags 下拉框保持现状

左侧继续由 `GatewayEndpointEditorViewModel.ComboOptions` 呈现不存在的绑定，不增加可见性过滤。现有 `IsDeleted` 状态标签与点击保存行为均保留。

## Risks / Trade-offs

- [风险] 隐藏项仍存在于右侧绑定集合中，集合数量与视觉数量不同。→ 本次仅改变展示语义，现有状态统计使用数据集合数量，不依赖可见项数量；通过契约测试锁定根容器过滤位置。
- [风险] 误把过滤规则加到左侧模板会破坏历史绑定管理。→ 测试同时断言左侧不存在项仍保持交互和状态样式。