## Context

Avalonia Fluent `ScrollViewer` 模板把 `PART_ScrollContentPresenter` 与 `PART_VerticalScrollBar` 作为同一模板网格中的独立子控件，并把 `ScrollViewer.Padding` 只绑定给内容呈现器。用户要求内容右边缘保持原位，只让滚动条处于红线位置。

## Goals / Non-Goals

**Goals:**
- 四个详情 Tab 的表单内容右边缘保持 20px inset。
- 主纵向滚动条保持在 ScrollViewer 原生模板层级，右侧 inset 为 12px。
- 不使用把 ScrollBar 平移出父级边界的方案，避免裁剪、遮盖和命中层级问题。

**Non-Goals:**
- 不调整左侧 Provider 目录滚动条。
- 不调整全局滚动条主题。
- 不调整测试响应文本框内部的嵌套滚动条。

## Decisions

1. `ScrollViewer.provider-tab-scroll` 使用 `Margin="0"`，让控件本身和模板内原生 ScrollBar 占据完整 Tab 内容宽度；滚动条因此位于用户红线处。
2. 同一 style 设置 `Padding="0,0,8,0"`。Avalonia Fluent 模板只把该 Padding 传给 `PART_ScrollContentPresenter`，所以内容右边缘向左保留 8px，而 `PART_VerticalScrollBar` 不受影响。
3. 不对模板内 ScrollBar 使用 `RenderTransform` 或额外 ZIndex。滚动条保持原生兄弟层级和模板裁剪范围，避免平移到父级边界外后被遮盖。
4. 契约测试断言 Margin=0、Padding 右侧=8，并禁止模板 ScrollBar TranslateTransform。

## Root Cause

- 第一版用外层右 Margin 8px，内容和滚动条一起向左。
- 第二版把 Margin 清零，内容和滚动条一起向右。
- 中间尝试只平移模板 ScrollBar，但它离开自身布局边界，存在用户指出的层级遮盖风险。
- 最终方案利用模板既有的内容/滚动条分层：ScrollViewer 保持全宽，Padding 只作用于 ScrollContent。

## Verification Geometry

- `TabRight = 1407`。
- `ScrollViewerRight = 1395`，原生 ScrollBar inset 为 12px。
- `ContentControlRight = 1387`，ScrollContent inset 保持 20px。

## Risks / Trade-offs

- [模板 Padding 绑定变化] → 契约测试、Release 构建和最终包实机几何读数共同覆盖。
- [不同 Tab 内容结构差异] → 四个 Tab 共用同一 ScrollViewer class，不对子内容分别定位。
