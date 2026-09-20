## Context

见 `proposal.md`。现有代码把 `AssistantView.AttachedToVisualTree` 直接映射到 `AssistantViewModel.Activate()`，而 `Activate()` 会订阅 Broker；`EnsureServiceAsync()` 还会在模型选择等非请求路径再次尝试订阅。已有规格只要求 Assistant 在需要用户决策时暂停/恢复，并要求 AI 在判断需要 Browser Bridge 后主动启用，不要求页面可见即建立决策通道。

## Goals / Non-Goals

**Goals:**

- 让 Broker 订阅与单次 Assistant 请求严格同生共灭。
- 消除导航、页面重挂载和模型选择造成的误导性订阅日志。
- 保留页面离开时对已领取决策请求的取消与幂等收敛。

**Non-Goals:**

- 不改变模型判断何时加载 Skill 的规则。
- 不改变 `browser.bridge_start` / `browser.bridge_stop` 的 Session 租约契约。
- 不重构 `UserDecisionBroker` 的 Claim/Submit/Cancel 并发模型。

## Decisions

### 1. 订阅入口放在 SendAsync 请求边界

`AssistantViewModel.SendAsync()` 在 `EnsureServiceAsync()` 成功并取得 `AssistantService` 后、调用 `service.SendAsync(text)` 前执行 `Activate()`；在 `finally` 中执行 `Deactivate()`。这样 Broker 在 AgentLoop 可能调用 `assistant.ask_user` 前已经可用，并在请求任何结束路径上解除。

不选择在 `SkillLoaded` 事件到达后再订阅，因为 Skill 工具事件与后续 `assistant.ask_user` 可能在同一 AgentLoop 中紧邻发生，UI 事件投影不应成为工具可用性的竞态前置条件；请求边界是最早且稳定的安全时点。

### 2. 页面挂载不再激活订阅

移除 `AttachedToVisualTree` 和 DataContext 切换时对 `Activate()` 的调用。页面卸载仍调用 `Deactivate()`，用于活动请求中已领取决策的取消；该方法保持幂等，因此请求 `finally` 再次调用安全。

### 3. 服务初始化不再隐式订阅

`EnsureServiceAsync()` 只确保 hosted services 可用并返回 `AssistantService`。打开模型选择器、加载历史或其他非发送路径不会产生订阅副作用。

## Risks / Trade-offs

- [用户在运行中离开 Assistant 页面后，本轮后续 AskUser 不再可展示] → 页面卸载会停用订阅并取消当前决策，符合既有“页面关闭不能永久阻塞 Agent Loop”约束；用户返回后可重新发送请求。
- [请求开始日志仍会显示一次订阅激活] → 这是实际 Agent 请求边界，含义准确；通过测试确保一次请求只订阅一次。
