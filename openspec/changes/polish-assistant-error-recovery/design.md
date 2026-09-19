## Context

当前 `AgentLoop` 对单次模型流中的任何异常立即生成 `TaskFailed`，`AssistantViewModel` 又把失败投影为普通状态文字，同时在消息区外维护一张带手动操作的错误卡片。透明外观协调器会按全局透明度同时降低语义表面的 Alpha，因此错误卡片可能失去稳定背景。模型错误已经具有 `ModelErrorKind` 和 HTTP 状态码分类，可直接作为重试判断事实源。

## Goals / Non-Goals

**Goals:**
- 在 Agent 模型步骤内实现总共最多 5 次、安全且可取消的自动尝试。
- 只在尚未产生可见流式事件时重试，避免重复正文和工具调用。
- 将终态失败统一投影为消息流异常气泡并支持历史重放。
- 将异常气泡材质与全局装饰透明度解耦，先产出可预览的中性深色正文效果。

**Non-Goals:**
- 不重试工具执行失败。
- 不对确定性 4xx 错误进行自动恢复。
- 本次不最终锁定异常正文的精确色值，预览后可继续微调视觉 token。

## Decisions

### 在 AgentLoop 内执行步骤级重试

`AgentLoop` 能同时看到结构化异常、流式事件是否已经开始以及用户取消信号，因此在这里实现重试可以覆盖所有模型客户端，并防止 UI 层重复发送整轮消息。每次尝试重新创建流枚举器和模型超时令牌；首次请求计为第 1 次，总数上限为 5。

只有 `ConnectionFailed`、`Timeout`、`RateLimited`、`ServerOverloaded` 和 `ServerError` 可重试。若一次尝试已经收到任何正文、思考、工具调用或完成事件，则失败后直接终止，不自动重放。相比在 ViewModel 中重新调用 `SendAsync`，该方案不会重复用户气泡或重新建立整轮会话。

### 结构化传递 Retry-After

模型客户端从 HTTP 响应头解析 `Retry-After` 并写入 `ModelClientException.RetryAfter`。AgentLoop 优先使用该值，同时设置合理上限；没有提示时按尝试序号使用递增退避。延迟函数通过可选委托注入，生产环境使用 `Task.Delay`，测试使用立即完成委托。

### TaskFailed 直接成为消息类型

新增 `ChatEntryKind.Error` 和 `ChatMessageViewModel.Error`。`TaskFailed` 结束当前过程块后追加 Error 条目。由于 TaskFailed 活动已经随会话持久化，历史载入继续通过现有事件投影恢复气泡，无需修改会话存储格式。ViewModel 的 `ErrorMessage`、`RetryCommand`、`DismissErrorCommand` 和独立错误卡片全部移除；服务不可用等 UI 捕获异常也直接追加 Error 条目。

### 阅读表面使用独立 Alpha 下限

新增异常气泡背景、边框和正文 token。异常表面仍保留淡红透明质感，但运行时 Alpha 不低于阅读下限；正文暂用中性深灰黑色 token、14px、普通字重和 22px 行高。红色只用于背景、描边和小图标，不用于正文。

## Risks / Trade-offs

- [服务端 Retry-After 过长] → 对自动等待设置上限，同时始终响应用户取消。
- [流式输出中途失败无法自动恢复] → 为避免重复内容和潜在工具行为，宁可明确失败；后续若协议支持断点续传再单独设计。
- [五次尝试增加失败耗时] → 确定性错误立即短路，递增等待保持有界并记录安全的结构化日志。
- [临时正文色仍需视觉调整] → 使用单独 token，预览后只改 token 即可，不改组件结构。
