## Context

底层 `assistant.ask_user` 工具由 ToolRegistry 直接注册，实际并不依赖 Skill 或 Browser Bridge；过度限制来自规格措辞与系统提示中的使用场景偏置。第一次 Approval Card 实现采用独立 Avalonia Window，真实交互虽能提交五类字段组合，但用户确认目标应是锚定在 AI 输入框上方的应用内悬浮卡片，而不是模态弹窗或消息流条目。

AskUser 会暂停当前 AgentLoop 等待结构化结果。在此期间禁止输入虽然安全，但体验压抑；直接把新消息注入当前轮次又会造成会话并发和上下文过时。成熟交互采用队列语义：用户继续输入，发送后先进入待发送区，等当前轮次完整结束再成为下一轮正式用户消息。

## Goals / Non-Goals

**Goals:**

- 让模型在用户明确要求或确有交互需要时直接调用 AskUser。
- 让 AskUser 与 Skill、Bridge、Chrome 生命周期解耦。
- 用输入框上方的悬浮 Approval Card 完成逐题输入，不创建第二顶层窗口。
- 保持输入框可用；运行中发送的消息进入当前会话临时队列。
- 队列项可删除，并在当前 Assistant 轮次完整结束后顺序出队。
- 保持四类字段、敏感信息边界、Broker Submit/Cancel 和请求级订阅生命周期稳定。

**Non-Goals:**

- 不增加新的 AskUser 字段类型或改变 `UserDecisionResult` 的值类型。
- 不把 AskUser 卡片写入会话消息历史。
- 本次不实现队列拖动排序、编辑、立即发送、Steer、Stop-and-Send 或跨重启持久化；这些能力记录到独立后续需求。
- 不修改 Browser Bridge 租约协议、Skill 内容加载协议或 Chrome Extension。

## Decisions

### 1. AskUser 是通用工具，而不是资料兜底工具

系统提示与工具描述明确：用户要求测试 AskUser、收集选择/数字/文本、澄清歧义或确认行动时可以直接调用。普通步骤“不强制询问”只表示默认不打扰，不构成禁止条件。Skill 与 Bridge 仅在任务本身需要领域知识或浏览器时加载。

### 2. 每个 UserDecisionField 对应一页

AskUser 状态 ViewModel 维护当前索引、步骤文本、前后导航、跳过、继续和提交状态。字段 ViewModel 保存真实输入，因此来回切换不丢值。最终仍由 `TryBuildResult` 一次性验证并构造原有字典结果。

### 3. 跳过只处理当前可选字段

可选字段可跳过并清空当前值；必填字段不可跳过。最后一页跳过后尝试提交整个请求。取消整个请求不再使用右上角关闭按钮，而是在 `allow_cancel=true` 时显示底部低强调取消操作。

### 4. 单选自动前进属于 View 交互

选择单选项后由卡片代码后置执行短延迟自动前进；状态 ViewModel 只提供确定性的 `TryAdvanceCurrentField`。多选、数字和文本等待用户点击继续。输入控件自身处理 Enter/Ctrl+Enter，Assistant 顶层不处理 Enter。

### 5. AskUser 使用 Assistant 内部悬浮层

AskUser 视图改为 `UserControl`，由 `AssistantView` 的 overlay 层锚定在输入容器正上方：

- 不进入 `Messages`；
- 不占用固定消息列表行；
- 不创建 Window 或第二 HWND；
- 卡片随输入容器位置和高度变化保持约 10–12px 间距；
- 卡片 `MaxWidth` 约 560，输入容器 `MaxWidth` 约 760，窄窗口自动收缩；
- 使用 DynamicResource、边框和轻量阴影表达悬浮层次，无页面遮罩。

`AssistantViewModel.PendingAskUser` 是唯一活动卡片来源。卡片完成后先验证当前 RequestId Ownership，再调用 Broker Submit/Cancel；请求结束、页面离开、会话切换或 Dispose 会幂等取消并清空卡片。迟到事件不得完成新请求。

### 6. 简版消息队列与当前轮次串行

当 `IsRunning=true` 时，用户点击发送不再被禁止，而是把文本捕获为带稳定 id、SessionId、创建时间和状态的队列项，清空输入框并显示在输入框上方。队列项在真正出队前不进入正式消息历史，可由用户删除。

AskUser 卡片提交只恢复当前 AgentLoop，不触发出队。只有当前 Assistant `SendAsync` 轮次完整结束后，队首才转为正式用户消息并启动下一轮。每次只执行一个轮次；正常完成后继续下一项，失败或用户停止时暂停自动出队并保留剩余项目。

简版 UI 只暴露顺序和删除。数据结构不以“不可编辑/不可排序”为不变量，为后续 Codex 风格编辑、排序、立即发送和 Steer 留出扩展空间。

### 7. 队列属于会话而不是当前视图

每个队列项记录创建时 SessionId。切换会话不得把旧队列发送到新会话；当前版本只在对应会话活动且没有运行中的轮次时自动排空。队列为内存状态，应用退出时不承诺恢复。

### 8. Avalonia 测试必须显式回到 UI 线程

初始化 Avalonia 后，任何创建 Window、Control、Geometry 或关闭窗口的断言都必须在 `Dispatcher.UIThread` 上执行。异步数据库准备不得依赖 xUnit continuation 恰好回到初始化线程。现有失败的生命周期测试作为回归入口。

## Risks / Mitigations

- **队列递归启动导致状态重入**：使用单一队列处理循环，不在 `finally` 中无界递归调用发送命令。
- **卡片或队列绑定错误会话**：所有完成和出队操作校验 RequestId、SessionId 与 Ownership。
- **队列失败后连续轰炸 Provider**：异常、取消或服务不可用时停止自动排空。
- **悬浮层遮住过多消息**：卡片和队列使用受控 MaxHeight，消息区仍可滚动，窄窗口下优先压缩内容而非扩大主窗口。
- **自动化输入污染键盘观察**：键盘正确性以自动化契约为主；桌面验收使用本地 `cua-driver`，先截图和 UIA，必要时才使用系统鼠标。

## Future Requirement

完整 Codex 风格队列记录在 `docs/superpowers/specs/2026-09-20-assistant-message-queue-requirements.md`。后续实施时应创建独立 Comet/OpenSpec change，不在本 change 内继续扩展。
