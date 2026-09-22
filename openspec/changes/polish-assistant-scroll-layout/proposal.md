## Why
助手顶部按钮存在绘制溢出，默认滚动条与透明主题不协调，消息流与输入卡之间的空隙导致滚动割裂。

## What Changes
- 修复新会话按钮右侧裁剪。
- 消息滚动条右移，使用透明轨道与主题色胶囊滑块。
- 消息视口向输入卡延伸 7px，待批准时保持正常边界。

## Capabilities
### New Capabilities
- 无新增业务能力。
### Modified Capabilities
- 无规范级行为变更；仅修复现有布局和外观，跳过 delta spec。

## Impact
仅 AssistantView 的局部样式、布局及控件测试，不改变请求、数据库和会话逻辑。
