## Context

见 `proposal.md`。`AssistantService.CurrentSession` 表示用户当前查看的会话，但 `SendAsync` 也把它当作正在运行的会话使用；在 await 和异步枚举期间切换会话后，两种生命周期发生分离。`AssistantViewModel.Project` 当前没有会话边界，任何收到的事件都会修改当前消息集合。

## Goals / Non-Goals

**Goals:**

- 固定每轮运行使用的 `AgentSession` 实例。
- 让实时事件与历史重放共享明确的当前展示会话边界。
- 保持后台旧运行可取消、可完成、可持久化。

**Non-Goals:**

- 不引入并行多轮运行。
- 不改变会话存储格式、模型协议或历史列表交互。
- 不在切换后自动生成旧会话标题。

## Decisions

1. `SendAsync` 在取得运行互斥锁后立即捕获 `runSession`，后续 `AgentLoop`、活动记录、保存、日志和失败事件只使用该局部引用。相比在每次保存时校验 `CurrentSession.Id`，固定对象能直接表达运行所有权，也覆盖模型创建 await 期间切换会话的情况。
2. `Project` 接收可选的 `viewedSessionId` 并在处理事件前校验 `SessionId`。实时流在 UI 调度时传入服务当前会话 id，历史重放传入载入会话 id；相比维护额外字段，这个无状态边界不会与服务当前会话失同步，也让所有事件来源复用同一过滤规则。
3. 直接调用 `Project` 的既有单元测试可省略 `viewedSessionId`，继续验证纯投影行为；会话隔离测试和真实 UI 调用显式传入当前查看会话。

## Risks / Trade-offs

- [切换后旧运行完成但不自动生成标题] → 标题是非关键增强，优先保证不对当前会话误操作；旧会话再次打开时仍保留完整内容。
- [调用方漏传当前查看会话导致事件未过滤] → 实时发送和历史重放两个生产入口均显式传参，并增加投影隔离回归测试。
