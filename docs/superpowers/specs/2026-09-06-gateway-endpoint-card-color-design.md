# 网关 Endpoint 卡片颜色调整设计

## 背景

网关页面左侧的 Endpoint 列表条目直接使用透明背景，叠加在玻璃面板上时与面板层级不够稳定，Azure、Ollama、OpenAI 三个卡片的颜色观感偏突兀。

## 目标

- 降低左侧 Endpoint 卡片的蓝色饱和度和视觉对比度。
- 保留 Endpoint 卡片与列表分隔线之间的层级关系。
- 不改变右侧 Combo 卡片、弹窗、布局或交互行为。

## 方案

在 `GatewayView.axaml` 内增加仅供 Endpoint 列表使用的局部样式：

- Endpoint 卡片使用现有的中性 `SurfaceBrush`，让卡片比外层玻璃面板更稳定、但不引入新的色相。
- 保留现有分隔线、内边距和动态资源机制。

不修改全局 `VisualTokens.axaml` 中的 `SurfaceSubtleBrush`，避免影响 Provider、模型选择器等其他界面。

## 验证

- 更新 `GatewayViewContractTests`，断言 Endpoint 条目使用新的局部样式。
- 运行 Gateway 视图契约测试、完整测试和 Release 构建。
- 发布桌面包后检查左侧三张卡片在玻璃背景上的层级和对比度。
