## Context

见 `proposal.md`。`AnchoredPopupWindow` 通过非模态 `Show(owner)` 展示，并设置 `ShowActivated=False`。该属性只阻止窗口首次显示时激活；用户点击浮窗后它仍会成为活动窗口。当前会话 cell 在 Click 中直接关闭浮窗，非模态窗口关闭流程不会像 `ShowDialog` 一样自动调用 owner 的 `Activate()`，因此 Windows 可能恢复 LoomX 之前的外部前台窗口。

## Goals / Non-Goals

**Goals:**

- 从浮窗内部完成会话选择后，先关闭浮窗，再恢复 LoomX owner 的激活状态。
- 保持外点关闭、Escape、窗口移动和 owner 关闭的现有焦点语义。

**Non-Goals:**

- 不把浮窗改为模态窗口。
- 不修改全局单实例激活或 Win32 前台抢占逻辑。

## Decisions

1. 在 `AnchoredPopupWindow` 提供面向内部命令完成场景的关闭方法，由浮窗自己持有并激活 owner。相比让 `AssistantHistoryPanel` 向上查找两个窗口，该边界能集中表达浮窗生命周期。
2. 激活 owner 必须发生在销毁活动浮窗之后。真实 Win32 采样表明，owned window 仍活动时调用 owner 的 `Activate()` 不会完成前台切换；先销毁浮窗，再在同一次用户 Click 中激活已捕获的 owner，才能覆盖 Windows 对前一外部窗口的默认恢复。
3. 只有历史会话 cell 选择路径使用该方法。通用 `Close()` 继续用于外点、Escape 和几何变化，避免用户切到其他应用后 LoomX 抢回焦点。

## Risks / Trade-offs

- [Risk] 关闭浮窗后 `ownerWindow` 会在 `Closed` 回调中清空。 → 关闭前捕获 owner 引用，关闭完成后使用该引用激活主窗口。
- [Risk] 平台窗口激活行为无法由纯单元测试完整模拟。 → 自动测试覆盖关闭顺序契约，发布包使用真实 Win32 前台窗口场景验证最终行为。
