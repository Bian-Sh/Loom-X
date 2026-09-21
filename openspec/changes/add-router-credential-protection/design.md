## Context

LoomX Plugin System 是 Router 的扩展系统，内置 AI 助手是 Router 的消费者而不是插件宿主。源码确认目前两类客户共享同一 Provider 请求执行后半段：

- **外部客户 / 网关**：`LoomXHost` 经 `ProtocolPassthroughClient` 构造上游请求，再调用 `IProviderExecutionPipeline`。
- **内置 AI 助手**：`AssistantModelClientFactory` 创建 `OpenAiCompatibleModelClient`，后者同样调用 `IProviderExecutionPipeline`；助手不应拥有绕过 Router Plugin Pipeline 的专用通道。
- **统一出站边界**：`ProviderExecutionPipeline.ExecuteAsync` 与 `ExecuteStreamingAsync` 在完整 `HttpRequestMessage` 构造后执行 `HttpClient.SendAsync`，是网关与助手共享的 Router Provider 执行边界。

既有助手保护（`SecretBoundary`、`SensitiveKeyPolicy`、`ToolArgumentSafety`、`AssistantSessionStore.SecretLeakScan`）继续保留，但它们属于助手自身安全策略，不是 Router Plugin 扩展点。本 change 依据 `.design/LoomX_Plugin_System_Design_CN.md`：脱敏由第一方 Credential Protection Router 插件承载；安全约束由 Router Contract 保证，数据安全类 Extension 失败必须 fail closed；正式修改 LoomX 前先经 PluginPlayground 验证。

## Goals / Non-Goals

**Goals:**

- 最小契约程序集与宿主侧 Plugin Runtime：发现、Manifest 验证、AssemblyLoadContext 动态加载、Router Extension 注册、同一 Pipeline 内按配置顺序执行、异常隔离。
- Request Pipeline 挂载到 Router 统一 Provider 出站边界，使网关和内置 AI 助手等 Router 客户自动获得相同能力。
- Credential Protection 第一方插件：Credential Detection、Plugin-owned Sensitive Rule、Mask/Placeholder、失败 fail closed。
- `LoomX.PluginPlayground` 验证项目：验证 Runtime/Pipeline 与 Sensitive Data 插件组合，验证用 Runtime 与正式迁入的是同一份实现。

**Non-Goals:**

- 在 `AgentLoop`、`AgentSession` 或 `AssistantSessionStore` 内挂载 Router Plugin。
- Settings UI / SettingsProvider / Avalonia 动态 XAML、Hot Reload、Restore / Echo Re-scrubbing、Tool Result Compression 插件、Response 流式处理、Plugin Marketplace、插件间依赖图与全局 Priority DSL。
- 本 change 不统一 Native Anthropic 的独立发送链；首版生产挂载覆盖经 `IProviderExecutionPipeline` 执行的 OpenAI/Ollama 兼容 Router 请求，后续统一 Anthropic 时复用同一 Contract。

## Decisions

### 1. 契约程序集 `LoomX.Plugin.Abstractions`

新增独立类库，仅包含：插件 Manifest 模型、`ILoomXPlugin`、Router Extension 接口、Pipeline Entry 描述、执行上下文与结果模型。目标框架与主项目一致（net10.0）。

- **为什么独立**：AssemblyLoadContext 类型身份共享要求宿主与插件引用同一个契约程序集；契约放主程序集会让插件被迫依赖宿主全部类型。
- **边界**：契约不引用 `LoomX.Harness`，插件也不得依赖 `AgentLoop`、`AgentSession` 或助手存储类型。

### 2. Plugin Runtime 放独立类库 `LoomX.PluginHost`

新增类库承载目录发现、Manifest 验证、ALC 加载、Extension 注册、Pipeline 编排与错误隔离。`LoomX` 主程序与 `LoomX.PluginPlayground` 都引用它。

- **为什么**：设计文档要求“先 Playground 验证再迁入”；Runtime 独立成库后，Playground 验证的就是最终迁入的同一份实现，消除验证与迁入之间的重复和漂移。

### 3. 生产挂载点：Router Request 出站边界

Credential Protection 以 `IRequestExtension` 注册到 `request` Pipeline。`ProviderExecutionPipeline.ExecuteAsync` 与 `ExecuteStreamingAsync` 在发送前读取请求正文并执行该 Pipeline：

```text
Gateway / 内置 AI 助手 / 其他 Router 客户
        ↓
完整 HttpRequestMessage
        ↓
Router Request Pipeline
        ↓
HttpClient.SendAsync
        ↓
外部 Provider
```

- Pipeline `Passed`：沿用原请求正文。
- Pipeline `Modified`：使用脱敏后的正文替换 `HttpContent`，同时保留原 Content Header（如 Content-Type）。
- Pipeline `Blocked` 或执行故障：抛出安全失败，禁止原始正文发送给外部 Provider。
- 请求无正文时直接通过。
- Authorization、API Key 和自定义 Header 由 Router Core 负责合法上游认证，不交给请求正文插件修改。

**为什么不挂 AgentLoop：** `AgentLoop` 是 Router 客户内部实现。把插件注入其中会让外部客户无法受益，并把 Router Plugin 架构中心错误地转移到 Agent 生命周期。

**为什么不挂 AssistantSessionStore：** 助手会话持久化属于 Assistant 领域；现有 `SecretLeakScan` 继续作为助手自身兜底，但不是 Router Persistence Pipeline。

Tool Result 中的敏感内容若将发送给外部模型，会作为下一次模型请求正文的一部分经过 Router Request Pipeline；因此内置助手与外部 Agent Client 均从 Router 边界获得一致的脱敏收益。未来 Tool Result Compression 也应由 Router 在可识别的请求/中间数据 Contract 中执行，而不是直接依赖 `AgentLoop`。

### 4. Credential Protection 插件形态

- 检测语义迁移自现有 `SensitiveKeyPolicy`（敏感名称集 + 值形态正则 + 自由文本内容检测），作为插件内置规则基线；规则数据 Plugin-owned，持久化到插件自有 JSON 文件，不写宿主核心配置。
- 脱敏替换统一为固定占位符 `***`。
- 插件声明 `credential.detect`/`credential.mask` 能力，并以 FailClosed 的 Request Extension 注册到 `request` Pipeline。
- Restore / Local Resolution 与 Echo Re-scrubbing 首版不做。

### 5. ALC 策略

每个插件一个 collectible `AssemblyLoadContext`，共享程序集（契约库、`System.*`、宿主已加载程序集）经 Default 上下文解析。首版不做 unload 强制验证（属 Hot Reload 后续 change），但结构上不阻碍后续加入。

### 6. 错误隔离与 fail closed

Pipeline Runner 捕获 Extension 异常并记录安全诊断；普通 Entry 失败仅记录并继续，数据安全类 Entry 失败时返回 Blocked。Router Provider 执行边界把 Blocked 映射为安全异常并终止请求，原始正文不得继续流向外部模型。

## Risks / Trade-offs

- [ALC 类型身份冲突导致插件无法加载] → 契约程序集唯一且经 Default 上下文共享解析；Playground 专项验证插件只引用契约类型。
- [读取并替换请求正文改变 HttpContent Header] → 替换内容时复制原始 Content Header，专项测试覆盖 Content-Type 保留。
- [数据安全插件失败导致请求不可用] → 这是 fail closed 的预期行为；记录仅含 Provider/Model/Pipeline/异常类型的安全摘要。
- [检测规则误判正常数据] → 规则 Plugin-owned 可禁用可增删；内置基线与现有 `SensitiveKeyPolicy` 语义一致。
- [流式请求] → 当前模型请求正文仍是一次性 JSON `HttpContent`，响应是否流式不影响发送前正文处理。
- [Native Anthropic 暂未经过共享 Provider Pipeline] → 在文档明确首版覆盖范围，后续统一发送链时复用相同 Request Pipeline。
- [Playground 验证与正式迁入重复工作] → Runtime 独立成库、两端共用同一份实现，验证即迁入。

## Open Questions

- 规则文件修改后的热生效时机：首版按“插件重载后生效”处理，随 Hot Reload change 一并解决，不影响本 change。
- Router Response 与结构化 Tool Result Extension 的强类型 Contract：本 change 不提前固化，后续结合流式响应和 Tool Result Compression 单独设计。
