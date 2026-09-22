# Provider 模型同步按钮刷新图标设计

## 目标

修正 Provider 配置页“模型”Tab 中“同步模型”按钮的图标显示。当前图标使用 `PathIcon` 填充开放几何，渲染结果接近实心圆；目标是显示清晰的刷新箭头。

## 方案

将按钮内部的 `PathIcon` 替换为 `Path Classes="icon-glyph"`，沿用现有刷新箭头几何。全局 `icon-glyph` 样式已经提供透明填充、圆角描边、统一描边宽度和次要文本颜色，因此不新增资源、不改变按钮尺寸或布局。

## 行为边界

- 保留 `SyncModelsCommand`、按钮文案“同步模型”和 `AutomationProperties.Name`。
- 不修改模型同步请求、状态反馈或其他 Provider/Model 控件。
- 不新增依赖或全局样式。

## 验证

- 检查 XAML diff，确认只有同步按钮的图标控件类型发生变化。
- 构建 `OllamaHub.Desktop` 项目，确保 XAML 编译通过。
