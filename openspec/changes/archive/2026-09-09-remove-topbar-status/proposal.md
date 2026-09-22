## Why

五个原型页面的顶栏右侧长期显示服务状态、快照状态或刷新按钮，造成视觉噪声并占用用户希望用于设置生效反馈的区域。统一移除这些常驻控件，让操作反馈气泡在顶栏右侧短暂浮现。

## What Changes

- 移除五个页面顶栏右侧的常驻状态提示、计数提示和刷新按钮。
- 将现有操作反馈气泡定位到顶栏右侧，并保持自动消失与无障碍播报。
- 清理因顶栏控件移除而失效的脚本引用。

## Capabilities

### New Capabilities

### Modified Capabilities

- `.design/pages/*/index.html`: 调整统一顶栏反馈展示要求。

## Impact

- 受影响文件：`.design/pages/overview/index.html`、`gateway/index.html`、`providers/index.html`、`activity/index.html`、`console/index.html`。
- 不涉及生产 API、依赖、数据存储或后端行为。
