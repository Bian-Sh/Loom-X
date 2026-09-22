## Context

LoomX Plugin System 是 Router 的扩展系统，内置 AI 助手是 Router 的消费者。外部网关客户与内置助手共享 `IProviderExecutionPipeline` 的 Provider 请求/响应边界；助手自身还存在 Tool Result 进入 Session/UI 的本地边界，以及会话 JSONL 的持久化边界。

本 change 将 Credential Protection 插件确立为唯一的凭据值检测、结构化 token 化与恢复实现。旧的 `SecretBoundary`、`BrowserSecretVault`、`BrowserSecretHarvester`、`AssistantSessionStore.SecretLeakScan` 已删除；原 `ToolArgumentSafety` 更名为 `ToolCallProjection`，仅保留工具协议投影职责；原 `SensitiveKeyPolicy` 更名为 `AssistantContentPolicy`，仅保留 TOML 本地配置暴露与 AskUser 禁止索取认证信息的产品策略。

## Goals / Non-Goals

**Goals:**

- 建立最小契约程序集与宿主侧 Plugin Runtime：发现、Manifest 验证、AssemblyLoadContext 动态加载、Pipeline 注册、顺序执行与异常隔离。
- 在 Provider request/response、Assistant Tool Result、Session persistence/history load 四个边界统一执行 Credential Protection。
- 使用长期有效的唯一结构化 token，支持跨会话、跨进程重启恢复历史内容。
- token 映射持久化到插件自有 SQLite；原值使用 Windows DPAPI 加密，数据库不保存明文。
- 普通 JSON、完整 SSE、流式 SSE 以及 tool arguments 中的 token 均可安全恢复；未知或伪造 token 保持原样。
- 数据安全类 Extension 全部 fail closed。

**Non-Goals:**

- 插件 Settings UI、Marketplace、Hot Reload、插件依赖图与全局 Priority DSL。
- 让 Credential Protection 取代工具参数公开投影、TOML 本地读取权限或 AskUser 产品校验；这些不是凭据 token 化职责。
- 首版统一 Native Anthropic 的独立发送链；后续统一发送链时复用同一 Contract。

## Decisions

### 1. 契约与 Runtime

`LoomX.Plugin.Abstractions` 仅包含 Manifest、Extension 接口、Pipeline 上下文与结果；`LoomX.PluginHost` 承载发现、ALC 加载、Pipeline 编排与 fail-closed 隔离。主程序与 `LoomX.PluginPlayground` 使用同一 Runtime 实现。

### 2. 四个生产边界

Credential Protection 注册四个 Extension：

| Pipeline | Extension | 行为 |
|---|---|---|
| `request` | `credential.request` | Provider 请求正文外发前 token 化 |
| `response` | `credential.response` | Provider 响应返回本地调用方前恢复 |
| `tool-result` | `credential.tool-result` | 工具结果进入 Session、事件和 UI 前 token 化 |
| `persistence` | `credential.persistence` | 会话 JSONL 每条记录写入前 token 化 |

历史会话读取逐行经过 `response` Pipeline 恢复。Tool Result Extension 通过 `AgentLoop` 注入的通用处理委托执行，Harness 不依赖 Plugin Runtime；Pipeline 失败时原始工具结果不得进入 Session。

### 3. 长期结构化 token

- token 格式：`{{LOOMX_CREDENTIAL_<20 位 Base32>}}`。
- 使用原值 SHA-256 哈希建立唯一约束，相同凭据跨引擎、跨重启复用稳定 token。
- 映射存储于插件自有 `credential-tokens.db`。
- 原值以 DPAPI CurrentUser 加密后存储；禁止把明文、请求正文、响应正文或 token 对应原值写入日志。
- token 长期有效，不使用进程内 TTL；用户在任意时刻查看历史会话时仍可恢复。
- 仅恢复 SQLite 中存在且能成功解密的本地签发 token；未知/伪造 token 原样保留。

### 4. JSON 与 SSE 恢复

- 普通 JSON 按字符串节点恢复并重新序列化。
- `tool_calls[].function.arguments` 按嵌套 JSON 恢复，正确处理引号、反斜杠与换行转义。
- SSE 按逻辑内容通道缓冲跨事件 token，覆盖 `delta.content` 与 tool arguments。
- 流结束仍存在未闭合 token 时 fail closed。
- 响应正文发生修改后移除 `Content-Length`、`Content-MD5`、`Digest`、`Content-Digest`、`ETag` 等失效实体头。

### 5. 删除重复凭据机制，保留非凭据职责

- 删除 `SecretBoundary` 及不可解析的 `secret://provider/...` 展示引用；配置工具仅返回 `api_key_configured`。
- 删除 Browser 专用 Vault/Harvester 与 `api_key_secret_ref`；Browser 工具返回结构化结果，由统一 Tool Result Pipeline token 化。模型回传 token 后，Provider Response Pipeline 在工具调用解析前恢复到统一 `api_key` 参数。
- `ToolCallProjection` 继续隐藏未知工具或无法公开的原始参数，保证历史、审批、UI 与下一轮请求的协议投影一致；它不检测凭据。
- `AssistantContentPolicy` 继续限制 TOML 本地配置暴露并禁止 AskUser 索取认证信息；它不是运行时凭据 token 化边界。

### 6. 错误隔离

普通 Extension 可 ContinueOnError；Credential Protection 的四个 Extension 均为 FailClosed。请求、响应、工具结果或持久化处理失败时只返回安全摘要并终止原始数据继续流动。

## Risks / Trade-offs

- SQLite/DPAPI 损坏或当前用户上下文变化会导致历史 token 无法恢复；按 fail closed 处理，不降级泄露原文。
- token 长期有效意味着数据库需作为用户配置数据备份；数据库不含明文，但仍应限制访问。
- Tool Result 在进入 Session 前处理增加一次序列化扫描，换取 UI、事件和内存历史不出现浏览器等工具返回的凭据明文。
- Native Anthropic 尚未统一到共享 Provider Pipeline，是明确的后续工作。

## Open Questions

- 规则文件热加载随 Hot Reload change 处理。
- token 撤销、轮换与数据库迁移策略后续单独设计；当前目标是稳定跨会话恢复。
