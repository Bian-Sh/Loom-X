## Context

见 `proposal.md`。`AnchoredPopupWindow` 通过非模态 `Show(owner)` 展示，并设置 `ShowActivated=False`。该属性只阻止首次显示时激活；用户点击浮窗后它仍会成为活动窗口。第一版修复在关闭后调用 owner 的 `Activate()`，只能修正最终状态，无法阻止 Windows 在两个动作之间短暂恢复外部窗口，因此产生明显闪烁。

## Goals / Non-Goals

**Goals:**

- 历史浮层不再作为普通应用窗口参与 Windows 激活切换。
- 保持浮层可越过主窗口边界、行内重命名、删除、轻点关闭和箭头定位行为。
- 会话选择期间 LoomX 持续位于外部应用之上，不允许先显示外部应用再抢回 LoomX。

**Non-Goals:**

- 不修改全局单实例激活或 Win32 前台抢占逻辑。
- 不使用 `SetForegroundWindow`、临时 Topmost 或关闭后的 `Activate()` 补偿。

## Decisions

1. 使用 Avalonia 原生 `Popup`，并显式设置 `ShouldUseOverlayLayer=False`，在桌面端使用平台浮动宿主；它可越过主窗口边界，但不作为普通 LoomX 窗口参与应用级激活回退。
2. 使用 `AnchorAndGravity`、底边锚点、向下重力及 `SlideX,FlipY` 约束保持现有定位能力；Popup 打开后根据实际屏幕坐标修正箭头横向位置。
3. `AssistantHistoryPanel` 选择会话时只把 `IsHistoryOpen` 置为 `false`，由双向绑定关闭 Popup，然后执行载入命令。删除与行内重命名继续留在 Popup 内完成。
4. 删除 `AnchoredPopupWindow` 及所有 owner 激活补偿，避免两个窗口激活动作之间出现外部应用中间帧。

## Risks / Trade-offs

- [Risk] 平台可能把 Popup 回退到 owner 的 overlay layer，导致无法越过窗口边界。 → 设置 `ShouldUseOverlayLayer=False`，并在 Windows 发布包中验证实际窗口边界。
- [Risk] 屏幕约束横向移动 Popup 后，固定箭头会偏离锚点。 → 打开后读取 Popup 内容与锚点的屏幕坐标，重新计算箭头边距。
- [Risk] 平台焦点行为无法由纯单元测试完整模拟。 → 自动测试锁定 Popup 宿主契约，发布包同时采样前台 HWND 序列并观察是否出现外部应用闪帧。
