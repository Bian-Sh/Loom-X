## Why

AI 助手历史会话使用普通独立顶层 `Window`。用户点击会话 cell 时，该窗口会先成为活动窗口；关闭后 Windows 会短暂恢复打开 LoomX 前的外部窗口。事后再次激活 LoomX 虽能修正最终前台窗口，但会产生清晰可见的外部应用闪帧。

## What Changes

- 历史会话浮层改用 Avalonia 原生 `Popup` 的平台浮动宿主，不再创建参与应用激活切换的普通 `Window`。
- 历史会话 cell 仅关闭 Popup 绑定状态，不再调用任何窗口激活补偿。
- 增加回归测试并通过真实桌面交互验证前台窗口归属。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `assistant-session-lifecycle`: 补充从历史会话浮窗载入会话时 LoomX 应保持前台的可见行为。

## Impact

- 影响 `AssistantView` 的历史 Popup 宿主与 `AssistantHistoryPanel` 的会话选择路径，并移除专用 `AnchoredPopupWindow`。
- 不改变公开 API、数据库 schema、会话文件格式或模型协议。
