## Context

LoomX 现有数据流中，敏感数据可能越过边界的三个关键位置已经源码确认：

- **Tool Result 边界**：`AgentLoop.ExecuteToolAsync` 产出 `ToolResult` 后经 `session.AddMessage(ChatMessage.ToolResult(...))` 进入会话历史，随后随 `ModelRequest` 推送外部模型。现有保护是 `ToolArgumentSafety`（隐藏工具参数）与工具自声明的安全输出契约，但没有统一的"出站前脱敏"机制。
- **持久化边界**：`AssistantSessionStore.SaveAsync` 将消息写入 JSONL，现有兜底是 `SecretLeakScan`（只识别 sk-/Bearer 两种形态，命中即拒绝落盘）。
- **Provider 执行边界**：`ProviderExecutionPipeline` 是网关与小助手共享的 Provider 请求后半段统一执行点，网关（`LoomXHost`）与 `OpenAiCompatibleModelClient` 都经它转发。

既有保护（`SecretBoundary`、`SensitiveKeyPolicy`、`ProtectedApiKeyStore`/DPAPI）继续保留。本 change 依据 `.design/LoomX_Plugin_System_Design_CN.md`：脱敏是 Router Plugin Pipeline 能力，由第一方 Credential Protection 插件承载；安全约束由 Router Contract 保证，数据安全类 Extension 失败必须 fail closed；正式修改 LoomX 前先经 PluginPlayground 验证。

## Goals / Non-Goals

**Goals:**

- 最小契约程序集与宿主侧 Plugin Runtime：发现、Manifest 验证、AssemblyLoadContext 动态加载、Extension 注册、同一 Pipeline 内按配置顺序执行、异常隔离。
- Pipeline 挂载点落在上述三个已验证的现有边界上，以最小切口接入，不改变现有组件的行为语义。
- Credential Protection 第一方插件：Credential Detection、Plugin-owned Sensitive Rule、Mask/Placeholder、Persistence Sanitization、失败 fail closed。
- `LoomX.PluginPlayground` 验证项目：验证 Runtime/Pipeline 与 Sensitive Data 插件组合（设计文档 Phase 1 + Phase 3 聚焦部分），验证用 Runtime 与正式迁入的是同一份实现。

**Non-Goals:**

- Settings UI / SettingsProvider / Avalonia 动态 XAML、Hot Reload、Restore / Echo Re-scrubbing、Tool Result Compression 插件、Plugin Marketplace、插件间依赖图与全局 Priority DSL（见 proposal.md 非目标）。

## Decisions

### 1. 契约程序集 `LoomX.Plugin.Abstractions`

新增独立类库，仅包含：插件 Manifest 模型、`ILoomXPlugin`、Extension 接口（Request / ToolResult / Persistence 三类扩展点）、Pipeline Entry 描述、执行上下文与结果模型。目标框架与主项目一致（net10.0）。

- **为什么独立**：AssemblyLoadContext 类型身份共享要求宿主与插件引用同一个契约程序集；契约放主程序集会让插件被迫依赖宿主全部类型。
- **备选**：契约放 `LoomX.Harness` → 否决，Harness 是 Agent 侧组件，契约属于 Router 层，且会引入循环依赖。

### 2. Plugin Runtime 放独立类库 `LoomX.PluginHost`

新增类库承载目录发现、Manifest 验证、ALC 加载、Extension 注册、Pipeline 编排与错误隔离。`LoomX` 主程序与 `LoomX.PluginPlayground` 都引用它。

- **为什么**：设计文档要求"先 Playground 验证再迁入"；Runtime 独立成库后，Playground 验证的就是最终迁入的同一份实现，消除验证与迁入之间的重复和漂移。
- **备选**：Runtime 先写进 Playground、验证后搬运 → 否决，搬运即二次实现。

### 3. Pipeline 挂载点：Tool Result 与 Persistence 优先

- **Tool Result Pipeline**：挂在 `AgentLoop` 中 `ToolResult.EnsureSafeFailure()` 之后、`session.AddMessage` 之前。这是"推送到 AI"的关键边界，直接覆盖用户痛点。
- **Persistence Pipeline**：挂在 `AssistantSessionStore.SaveAsync` 写入前。现有 `SecretLeakScan` 兜底保留，作为 Pipeline 之外的最后一道防线，不删除。
- **Request/Response Pipeline**：扩展点在契约中定义，首版只声明不挂载（Provider 请求本就需要携带 API Key 转发给正确的上游，不存在"推送 AI 泄露"语义；挂载留待后续 change 按源码进一步验证）。

### 4. Credential Protection 插件形态

- 检测语义迁移自现有 `SensitiveKeyPolicy`（敏感名称集 + 值形态正则 + 自由文本内容检测），作为插件内置规则基线；规则数据 Plugin-owned，持久化到插件自有 JSON 文件，不写宿主核心配置。
- 脱敏替换统一为固定占位符 `***`。
- 插件声明 `credential.detect`/`credential.mask` 能力，并被标记为数据安全类 Entry，触发 fail closed 策略。
- Restore / Local Resolution 与 Echo Re-scrubbing 首版不做（设计文档列为"可能包含"，按 ponytail 最小可用原则延后）。

### 5. ALC 策略

每个插件一个 collectible `AssemblyLoadContext`，共享程序集（契约库、`System.*`、宿主已加载程序集）经 Default 上下文解析。首版不做 unload 强制验证（属 Hot Reload 后续 change），但结构上不阻碍后续加入。

### 6. 错误隔离与 fail closed

Pipeline Runner 捕获 Extension 异常并记录诊断；按 Extension Point 失败策略分流：普通 Entry（如 Observability）失败仅记录并继续；数据安全类 Entry 失败时 Pipeline 返回 Blocked 结果，调用方映射为安全失败（如 `ToolResult.SafeFail`、跳过本次落盘），原始数据不得继续流动。

## Risks / Trade-offs

- [ALC 类型身份冲突导致插件无法加载] → 契约程序集唯一且经 Default 上下文共享解析；Playground 专项验证插件只引用契约类型。
- [挂载点侵入 Harness/Store 改变现有行为] → 最小切口接入既有边界，所有现有测试保持绿色；Pipeline 为空时行为与现状完全一致。
- [检测规则误判正常数据] → 规则 Plugin-owned 可禁用可增删；内置基线与现有 `SensitiveKeyPolicy` 语义一致，沿用其已验证的测试用例。
- [每条 Tool Result 都过检测带来性能开销] → 检测为纯内存名称/正则匹配，开销可忽略；如后续出现热点再加采样或开关。
- [Playground 验证与正式迁入重复工作] → Runtime 独立成库、两端共用同一份实现，验证即迁入。

## Open Questions

- 规则文件修改后的热生效时机：首版按"插件重载后生效"处理，随 Hot Reload change 一并解决，不影响本 change 的 spec 与任务拆分。