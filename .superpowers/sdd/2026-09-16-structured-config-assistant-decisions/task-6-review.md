# Task 6 首轮任务级审查

### Spec Compliance

- ❌ **Issues found**：工具注册、强类型转换、基本等待/恢复和取消链路已实现，但仍有四项阻断问题：自由文本结果进入 ToolResult/Session；AskUser 内容策略无法拒绝不含密钥关键词的完整 TOML、请求/响应正文或自定义 Header，且运行时未封闭 Schema 属性；跨多次 `yield` 的 `AsyncLocal` owner 传播不可靠；工具继承默认 30 秒超时。
- ⚠️ **Cannot verify from diff**：最终提交无法独立证明实际 RED→GREEN 执行顺序及报告中的 43/43、69/69、835/835、format 和 diff-check；审查依据为 task brief、实施报告、review package 与针对具体风险的未改动代码检查。
- 未发现数据库路径、Catalog、`config.toml`、环境变量、搜索 Provider、审批语义或其他越界生产代码改动。

### Strengths

- `LoomX/Assistant/AssistantTools.cs:17-28,173-225` 的注册入口、工具名称、Read 风险与四类字段 Schema 基本符合要求。
- `LoomX/Assistant/AssistantTools.cs:28-48,61-170` 将 JSON 转换为 Task 5 强类型模型，异步传递工具 CancellationToken，失败结果不回显原始参数。
- `LoomX/LoomXHost.cs:77,102-104` 使用同一 singleton Broker 完成 DI 与工具注册。
- `LoomX.Tests/Assistant/AssistantToolsTests.cs:198-215` 验证显式取消严格返回 `{"cancelled":true,"values":{}}`。
- `LoomX.Tests/Assistant/AssistantServiceTests.cs:227-324` 覆盖 pending 捕获、Submit 恢复、Session 工具消息、第二轮模型请求、最终回答，以及 Stop、新会话和外部取消令牌。

### Issues

#### Critical (Must Fix)

1. **Text 提交原文进入 ToolResult、Session 和后续模型上下文**
   - 位置：`LoomX/Assistant/AssistantTools.cs:158-170`；相关测试 `LoomX.Tests/Assistant/AssistantToolsTests.cs:147-158`；未改动消费链 `LoomX.Harness/AgentLoop.cs:226-234`。
   - 问题：`SerializeResult` 无差别序列化所有提交值，包括 Text 字段的用户自由文本；AgentLoop 会把该内容持久化到 Session、通过消息事件暴露并发送给下一轮模型。
   - 影响：直接违反“ToolResult、日志、Toast 不得包含用户自由文本结果”的绑定边界。
   - 修复方向：按字段类型生成安全结果；选择只返回 id、数字返回数值，Text 只返回不含内容的状态或安全占位；增加 ToolResult、Session、消息事件均不含原文的测试。

2. **AskUser 内容边界与运行时属性契约不封闭**
   - 位置：`LoomX/Assistant/AssistantTools.cs:32-34,61-149,175-223`；未改动策略 `LoomX/Assistant/UserDecisions/UserDecisionModels.cs:707-731`、`LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:31-92`。
   - 问题：现有策略主要识别凭据模式和敏感关键词，完整但无密钥的 TOML、JSON 请求正文或 `X-Tenant: acme` 等 Header 形态可进入 pending；Schema 声明 `additionalProperties:false`，但运行时静默忽略未知属性。
   - 影响：完整正文、TOML 或 Header 值可能展示给用户，且工具 Schema 与运行时行为不一致。
   - 修复方向：增加 AskUser 专用内容策略，拒绝 HTTP Header 行及 JSON/TOML/请求正文块；显式校验顶层、field、option 的允许属性集合；补充无敏感关键词正文、TOML、Header 和未知属性测试。

#### Important (Should Fix)

1. **跨 async iterator `yield` 的 `AsyncLocal` owner 传播不可靠**
   - 位置：`LoomX/Assistant/AssistantTools.cs:11-58`、`LoomX/Assistant/AssistantService.cs:241-246,281-285`。
   - 问题：`SendAsync` 在一次 MoveNext 中建立 scope 后多次 `yield return`，后续工具执行由调用方 ExecutionContext 再进入，不能保证仍继承该 owner；工具可能使用随机 fallback owner。现有取消测试主要由 CancellationToken 收敛，未验证 Request 与 CancelOwner 使用同一 owner。
   - 影响：`PendingUserDecision.OwnerId` 不稳定，finally 的 `CancelOwner(ownerId, ...)` 可能匹配不到请求。
   - 修复方向：显式传递 owner 或在每次驱动嵌套 enumerator 的 MoveNext 前重建 scope；使用 spy broker 验证同一 Run 多次 AskUser 使用同一 owner，且 finally 精确取消。

2. **人工决策工具继承默认 30 秒工具超时**
   - 位置：`LoomX/Assistant/AssistantTools.cs:22-50`；未改动默认值 `LoomX.Harness/ToolRegistry.cs:33` 与超时处理 `LoomX.Harness/AgentLoop.cs:400-410`。
   - 问题：`assistant.ask_user` 未设置 Timeout，用户思考超过 30 秒后会被 AgentLoop 取消，并返回普通工具超时失败。
   - 影响：正常人工决策可能提前失败，且不能返回约定的结构化取消结果。
   - 修复方向：为该工具显式使用适合人工交互的等待时限或无限等待，主要依赖 Run CancellationToken/CancelOwner 收敛；增加超过默认工具时限仍 pending、随后可 Submit，以及生命周期取消立即结束的测试。

#### Minor (Nice to Have)

- 无。

### Assessment

**Task quality:** Needs fixes

**Reasoning:** 注册、转换和基本恢复链路完成度较高，但两项信息边界违规以及 owner/超时生命周期缺陷会使数据安全与人工交互可靠性失守，Task 6 当前不能批准。

**Checks run:** 审查代理只读检查 task brief、实施报告与完整 diff package，并针对 owner、CancellationToken、工具超时和内容安全风险检查 `AgentLoop`、`ToolRegistry`、`UserDecisionBroker`、`UserDecisionModels` 与 `SensitiveKeyPolicy`；未重跑测试。
