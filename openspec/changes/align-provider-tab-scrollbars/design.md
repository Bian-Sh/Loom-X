## Context

Provider 详情区由外层 `Border`、标题和 `TabControl` 组成，四个 Tab 分别拥有外层 `ScrollViewer`。用户截图显示，先前为追求 20px 数值而增加的 8px 页面补偿使滚动条落在红色参考线左侧；目标位置实际是 `TabControl` 模板默认内容边界。

## Goals / Non-Goals

**Goals:**
- 四个详情 Tab 的主纵向滚动条共用同一定位规则。
- 滚动条对齐用户截图标出的红色参考线。
- 保持标题、表单内容和嵌套响应文本框的既有交互。

**Non-Goals:**
- 不调整左侧 Provider 目录滚动条。
- 不调整全局滚动条主题。
- 不调整测试响应文本框内部的嵌套滚动条。

## Decisions

1. 保持详情内容外层 `Border` 的右侧 Padding 为 0，并给标题区域保留 18px 右边距，使 `TabControl` 的可用区域右边缘与详情面板右边缘一致。
2. 四个 Tab 的外层 `ScrollViewer` 继续共用 `provider-tab-scroll` class，但页面局部样式改为 `Margin="0"`。不再叠加 8px 右侧 Margin，直接使用 Avalonia `TabControl` 模板自身的内容内缩，使滚动条向右移动到截图红线位置。
3. 视图契约测试断言共享样式的 Margin 为 0、四个 class 使用点以及详情容器右侧 Padding，避免后续只修改部分 Tab或再次叠加页面边距。

## Root Cause

- 截图中现有滚动条位于约 `x=548..553`，红色参考线位于约 `x=559..564`。
- 先前新增的 8 DIP 右侧 Margin 在当前显示缩放下对应约 10 个物理像素，正是滚动条与参考线之间的主要偏差。
- 因此根因是重复补偿：`TabControl` 已有模板内缩，页面又额外增加了 8px。

## Risks / Trade-offs

- [`TabControl` 默认模板变化影响位置] → 契约测试锁定页面不再额外补偿，并使用重新发布后的桌面端界面复核。
