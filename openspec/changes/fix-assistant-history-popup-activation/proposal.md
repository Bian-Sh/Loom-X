## Why

AI 助手历史会话使用独立顶层浮窗。用户点击会话 cell 后，浮窗关闭时没有把前台窗口恢复给 LoomX，Windows 会重新激活打开 LoomX 前的外部窗口，导致 LoomX 被 Codex 等应用覆盖。

## What Changes

- 历史会话 cell 完成选择并关闭浮窗时，显式把窗口激活权交还给 LoomX 主窗口。
- 外点、主窗口移动、Escape 等普通关闭路径保持现有行为，不从其他应用抢回焦点。
- 增加回归测试并通过真实桌面交互验证前台窗口归属。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `assistant-session-lifecycle`: 补充从历史会话浮窗载入会话时 LoomX 应保持前台的可见行为。

## Impact

- 影响 `AnchoredPopupWindow` 的主动关闭入口与 `AssistantHistoryPanel` 的会话选择路径。
- 不改变公开 API、数据库 schema、会话文件格式或模型协议。
