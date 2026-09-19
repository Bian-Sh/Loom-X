## Context

Provider 详情区当前由一个带左右 18px 内边距的外层 `Border` 包含标题和 `TabControl`，四个 Tab 分别拥有外层 `ScrollViewer`。为了让滚动条相对面板外边缘精确保持 20px，需要统一处理详情区右侧布局，而不是分别写不同数值。

## Goals / Non-Goals

**Goals:**
- 四个详情 Tab 的主纵向滚动条共用同一定位规则。
- 滚动条相对右侧详情面板边缘向左 20px。
- 保持标题、表单内容和嵌套响应文本框的既有交互。

**Non-Goals:**
- 不调整左侧 Provider 目录滚动条。
- 不调整全局滚动条主题。
- 不调整测试响应文本框内部的嵌套滚动条。

## Decisions

1. 将详情内容外层 `Border` 的右侧 Padding 从 18px 改为 0，并给标题区域保留 18px 右边距，使 `TabControl` 的可用区域右边缘与详情面板右边缘一致。
2. 为四个 Tab 的外层 `ScrollViewer` 添加统一 `provider-tab-scroll` class。Avalonia 当前 `TabControl` 模板已提供 12px 内容内缩，因此页面局部样式设置 `Margin="0,0,8,0"`，与模板内缩合计后使滚动条相对面板边缘精确向左 20px。
3. 使用视图契约测试断言共享样式、四个 class 使用点以及详情容器右侧 Padding，避免后续只修改部分 Tab 或重复叠加边距。

备选方案是仅给现有 `ScrollViewer` 增加 2px 右边距以叠加当前 18px Padding，但该方案把 20px 约束拆散到父子两层，语义不清晰且更容易被后续布局调整破坏。

## Risks / Trade-offs

- [TabControl 可用宽度增加 18px] → 四个滚动区域再统一收进 20px，最终内容宽度只比现状减少 2px；标题区域仍保留原有 18px 右侧留白。
- [默认 Tab 模板变化影响位置] → 契约测试锁定页面补偿值，并通过打包后的 UIA 几何读数验证面板右边缘与滚动条右边缘相差 20px。
