## Context

LoomX Plugin System 是 Router 的扩展系统，内置 AI 助手和外部 Agent Client 都只是 Router 客户。插件只处理 Router 拥有的 Provider request/response 传输边界，不进入 `AgentLoop`、`AgentSession`、助手 UI 或会话 JSONL。

本 change 实现 Credential Protection Router 插件。旧的 `SecretBoundary`、`BrowserSecretVault`、`BrowserSecretHarvester`、`AssistantSessionStore.SecretLeakScan` 已删除；原 `ToolArgumentSafety` 更名为 `ToolCallProjection`，仅保留工具协议投影职责；原 `SensitiveKeyPolicy` 更名为 `AssistantContentPolicy`，仅保留 TOML 本地配置暴露与 AskUser 禁止索取认证信息的产品策略。这些助手侧行为不是 Router Plugin Extension。

## Goals / Non-Goals

**Goals:**

- 建立最小契约程序集与宿主侧 Plugin Runtime：发现、Manifest 验证、AssemblyLoadContext 动态加载、Pipeline 注册、顺序执行与异常隔离。
- 在 Router Provider request/response 两个生产边界执行 Credential Protection。
- 使用长期有效的唯一结构化 token，支持跨请求、跨客户端会话和进程重启恢复 Provider 回显内容。
- token 映射持久化到插件自有 SQLite；原值使用 Windows DPAPI 加密，数据库不保存明文。
- 普通 JSON、完整 SSE、流式 SSE 以及 tool arguments 中的 token 均可安全恢复；未知或伪造 token 保持原样。
- 数据安全类 Extension 全部 fail closed。

**Non-Goals:**

- 对内置 AI 助手的内存消息、UI 展示或历史会话文件进行脱敏；这属于助手产品自身的数据与隐私策略。
- 控制其他 Agent Client 的 UI、日志、截图或本地会话存储。
- 在当前 change 新增独立 ToolResult Pipeline。工具结果随下一次模型请求进入 Router Request Pipeline，已受统一保护；未来 RTX 压缩若需要结构化识别 `role=tool`，再在 Router Request 内增加阶段或独立扩展点。
- 插件 Settings UI、Marketplace、Hot Reload、插件依赖图与全局 Priority DSL。
- 让 Credential Protection 取代工具参数公开投影、TOML 本地读取权限或 AskUser 产品校验。
- 首版统一 Native Anthropic 的独立发送链；后续统一发送链时复用同一 Contract。

## Decisions

### 1. 契约与 Runtime

`LoomX.Plugin.Abstractions` 仅包含 Manifest、Request/Response Extension、Pipeline 上下文与结果；`LoomX.PluginHost` 承载发现、ALC 加载、Pipeline 编排与 fail-closed 隔离。主程序与 `LoomX.PluginPlayground` 使用同一 Runtime 实现。

### 2. 两个 Router 生产边界

Credential Protection 注册两个 Extension：

| Pipeline | Extension | 行为 |
|---|---|---|
| `request` | `credential.request` | Provider 请求正文外发前 token 化 |
| `response` | `credential.response` | Provider 响应返回 Router 客户前恢复 |

请求正文中的用户消息、Assistant 内容和 Tool Result 不按来源分别挂载插件。它们在序列化成 Provider 请求后统一流经 Request Pipeline，因此内置助手与外部 Agent 客户端获得一致保护。

### 3. 长期结构化 token

- token 格式：`{{LOOMX_CREDENTIAL_<20 位 Base32>}}`。
- 使用原值 SHA-256 哈希建立唯一约束，相同凭据跨引擎、跨重启复用稳定 token。
- 映射存储于插件自有 `credential-tokens.db`。
- 原值以 DPAPI CurrentUser 加密后存储；禁止把明文、请求正文、响应正文或 token 对应原值写入日志。
- token 长期有效，不使用进程内 TTL；跨请求与重启后仍可恢复 Provider 回显。
- 仅恢复 SQLite 中存在且能成功解密的本地签发 token；未知或伪造 token 原样保留。

### 4. JSON 与 SSE 恢复

- 普通 JSON 按字符串节点恢复并重新序列化。
- `tool_calls[].function.arguments` 按嵌套 JSON 恢复，正确处理引号、反斜杠与换行转义。
- SSE 按逻辑内容通道缓冲跨事件 token，覆盖 `delta.content` 与 tool arguments。
- 流结束仍存在未闭合 token 时 fail closed。
- 响应正文发生修改后移除 `Content-Length`、`Content-MD5`、`Digest`、`Content-Digest`、`ETag` 等失效实体头。

### 5. 助手职责与复用边界

- `AgentLoop`、`AgentSession`、`AssistantSessionStore` 和 Assistant UI 不依赖 Plugin Runtime，也不挂载 Router Pipeline。
- 内置助手若要在屏幕、历史会话或本地日志中显示 `***`，应实现独立的“数据与隐私”策略；可复用 Credential Protection 的规则思想或提取出的纯检测契约，但不能调用 Router 插件改变会话语义。
- 删除 `SecretBoundary` 及不可解析的 `secret://provider/...` 展示引用；配置工具仅返回 `api_key_configured`。
- 删除 Browser 专用 Vault/Harvester 与 `api_key_secret_ref`；Browser 工具返回值是否在助手 UI 中打码，由助手自身策略决定。该结果进入下一次 Provider 请求时，Router Request Pipeline 负责外发脱敏。
- `ToolCallProjection` 继续保证工具参数协议投影一致；它不检测凭据。
- `AssistantContentPolicy` 继续限制 TOML 本地配置暴露并禁止 AskUser 索取认证信息；它不是 Router token 化边界。

### 6. RTX 压缩的未来位置

RTX/Tool Result Compression 属于 Router 插件能力。首选在 Request Pipeline 内解析 Provider 请求结构、识别 Tool Result 并压缩，再交给 Credential Protection 等 Entry 顺序执行。只有当多个 Router 功能确实需要独立的结构化 Tool Result 生命周期时，才新增 Router `ToolResultPipeline`；不得把挂载点放入内置助手的 `AgentLoop`。

### 7. 错误隔离

普通 Extension 可 ContinueOnError；Credential Protection 的 request/response Extension 均为 FailClosed。请求 token 化或响应恢复失败时只返回安全摘要并终止原始数据继续流动。

## Risks / Trade-offs

- SQLite/DPAPI 损坏或当前用户上下文变化会导致 token 无法恢复；按 fail closed 处理，不降级猜测或泄露原文。
- token 长期有效意味着数据库需作为用户配置数据备份；数据库不含明文，但仍应限制访问。
- 助手 Session 与历史文件不再由 Router 插件处理；如果产品要求防肩窥或历史文件打码，必须单独实现和测试助手隐私选项。
- Native Anthropic 尚未统一到共享 Provider Pipeline，是明确的后续工作。

## Open Questions

- 规则文件热加载随 Hot Reload change 处理。
- token 撤销、轮换与数据库迁移策略后续单独设计。
- RTX 压缩进入 Request Pipeline 后，根据解析与排序需求评估是否值得新增 Router ToolResult 扩展点。
