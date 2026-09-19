## Why

AI 助手正文流输出期间允许用户切换或新建会话，但当前运行的活动记录、持久化与 UI 投影仍会读取可变的当前会话，导致旧运行污染新查看会话，甚至把完整回答保存到错误会话。已有跳过回归测试已经固化该缺陷，现在需要恢复真实会话生命周期隔离。

## What Changes

- 每轮发送在启动时固定其运行会话，后续活动记录、增量保存、最终保存和持久化失败事件始终归属该会话。
- 桌面端只投影当前展示会话的实时事件；切换会话后，旧运行可以继续完成，但不得修改新会话的消息流。
- 启用既有服务回归测试，并补充 UI 投影隔离测试。

## Capabilities

### New Capabilities

- `assistant-session-lifecycle`: 规定 AI 助手运行会话在流式输出期间的持久化归属与当前视图隔离行为。

### Modified Capabilities

无。

## Impact

- 影响 `AssistantService` 的发送与持久化生命周期。
- 影响 `AssistantViewModel` 的会话切换和 `AgentEvent` 投影边界。
- 不改变公开 API、数据库 schema、会话文件格式或模型协议。
