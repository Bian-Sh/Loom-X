## Context

LoomX Plugin System 是 Router Provider 执行核心的扩展系统。外部 Agent Client 经对外 HTTP Server、鉴权、Combo 路由和协议适配进入该核心；内置 AI 助手不经过这些外部接入层，而是直接依赖 Plugin Runtime 装配的共享 `IProviderExecutionPipeline`。插件只处理 Provider request/response 传输边界，不进入 `AgentLoop`、`AgentSession`、助手 UI 或会话 JSONL。

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
- 让内置 Agent 直接使用 Anthropic 原生协议 Provider；当前内置 Agent 仍只创建 OpenAI 兼容模型客户端，后续单独实现对应 `IModelClient`。

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

### 4. Placeholder 协议完整性与受限归一化

结构化 token 会经过生成式模型，不能假设模型一定逐字符复制。Credential Protection 将 Prompt 约束视为 placeholder 协议的必选组成部分，同时以确定性解析、精确查表和 fail-closed 作为真正安全边界。

- Request Pipeline 完成 token 化后，若最终 Provider 请求正文含 LoomX placeholder，MUST 在发送前临时注入 placeholder 完整性指令；该指令不是可独立关闭的增强项。
- 完整性指令只声明 placeholder 是不透明、不可变的本地凭据引用：模型可以按目标 JSON、Header、URL、Shell 或 Tool Call 语法放置完整引用，但不得修改、翻译、拆分、转义、重新格式化或猜测其内部字符。
- 指令仅存在于本次 Provider 请求，不改写客户端原始 system message、Agent Session 或会话 JSONL；UI 与插件说明 MUST 明确披露插件会修改发送给 Provider 的系统/开发者指令，且注入内容不含凭据明文或 token 映射。
- placeholder 候选仅允许受限归一化：ASCII 大小写折叠，以及 token 语法内部明确允许的 ASCII 空白；禁止对整段响应全局删空白、Unicode 相似字符折叠、`O/0` 或 `I/1` 替换、缺字补全、编辑距离或其他模糊猜测。
- 归一化后仍必须满足固定前缀、固定分隔符、固定 Base32 字符集和长度，并精确命中本地 SQLite 中唯一映射；未知、残缺、歧义或不可解密 token 不得猜测恢复。
- Credential Protection 只规范和替换 placeholder 自身跨度，不修改外围引号、反引号、Header 前缀、URL、Shell 语法或其他字符。外围语法是否合法，由 JSON 解析器、Tool Schema、Tool Executor 或 Shell/目标协议负责。
- JSON 结构定界引号由解析与重新序列化管理；如果 JSON 解析后引号仍属于字段值，它就是实际数据，插件不得擅自删除。强类型凭据字段中的额外包装应由字段校验拒绝并要求 Agent 重试。

### 5. JSON 与 SSE 恢复

- 普通 JSON 按字符串节点恢复并重新序列化。
- `tool_calls[].function.arguments` 按嵌套 JSON 恢复，正确处理引号、反斜杠与换行转义。
- SSE 按逻辑内容通道缓冲跨事件 token，覆盖 `delta.content` 与 tool arguments；归一化状态必须能够跨网络 chunk 和 SSE event 延续。
- 流结束仍存在未闭合 token、token 异常超长或恢复后破坏 JSON 时 fail closed。
- 流式长度保护只计算实际未闭合 placeholder 候选，不得把候选之前的普通长文本计入 token 长度。
- 响应正文发生修改后移除 `Content-Length`、`Content-MD5`、`Digest`、`Content-Digest`、`ETag` 等失效实体头。

### 6. 启用、停用、卸载与历史兼容

Credential Protection 的“主动保护新内容”和“解析既有 placeholder”是两个不同生命周期，不得由一个普通启停开关同时关闭。

| 状态 | 新明文检测与 token 化 | 既有 placeholder 解析/恢复 | 完整性 Prompt | Vault |
|---|---|---|---|---|
| 完整保护 | 开启 | 开启 | 检测到 placeholder 时强制注入 | 保留 |
| 兼容解析（暂停主动保护） | 关闭 | 开启 | 检测到 placeholder 时仍强制注入 | 保留 |
| Runtime 已卸载 | 不可用 | 不可用 | 不可用 | 默认保留但暂不可使用 |

- UI 中“禁用保护”只能进入兼容解析模式：停止为新明文创建 token，但历史会话、Tool Call、外部客户端缓存中的本地签发 token 继续有效。
- 暂停主动保护 MUST 强警告新请求及历史会话中保存的明文凭据可能直接发送给 Provider；兼容解析只能保证已经存在的 placeholder，不会追溯保护客户端本地保存的明文。
- 只要最终 Provider 请求仍含 LoomX placeholder，解析、受限归一化、完整性 Prompt 与 Response 恢复必须继续工作。
- Credential Protection 应作为受保护的第一方系统插件，不提供无提示的一键卸载。Vault 中存在映射时，卸载前 MUST 明确警告历史引用将暂时失效并要求二次确认。
- 卸载插件代码与销毁 Credential Vault 必须是两个独立操作。卸载默认保留 Vault，以便重新安装兼容版本后恢复历史引用；销毁 Vault 会使历史 placeholder 永久失效，必须使用更高级别的不可逆确认，并优先提供加密备份能力。
- 即使本机扫描未发现 placeholder，也不能据此宣称安全卸载，因为外部 Agent Client、导出文件、备份或日志可能保存引用。

### 7. 助手职责与复用边界

- `AgentLoop`、`AgentSession`、`AssistantSessionStore` 和 Assistant UI 不直接调用插件或挂载 Pipeline；内置助手的模型网络边界通过共享 `IProviderExecutionPipeline` 有意依赖 Plugin Runtime 装配的 Request/Response Pipeline。
- 内置助手绕过对外 HTTP Server、Endpoint 鉴权与 Combo 路由，直接选择 Provider/Model；因此外部 Router Endpoint 尚未完成配置时，内置助手仍可使用已配置的 Provider，并继续获得插件能力。
- 内置助手若要在屏幕、历史会话或本地日志中显示 `***`，应实现独立的“数据与隐私”策略；可复用 Credential Protection 的规则思想或提取出的纯检测契约，但不能调用 Router 插件改变会话语义。
- 删除 `SecretBoundary` 及不可解析的 `secret://provider/...` 展示引用；配置工具仅返回 `api_key_configured`。
- 删除 Browser 专用 Vault/Harvester 与 `api_key_secret_ref`；Browser 工具返回值是否在助手 UI 中打码，由助手自身策略决定。该结果进入下一次 Provider 请求时，Router Request Pipeline 负责外发脱敏。
- `ToolCallProjection` 继续保证工具参数协议投影一致；它不检测凭据。
- `AssistantContentPolicy` 继续限制 TOML 本地配置暴露并禁止 AskUser 索取认证信息；它不是 Router token 化边界。

### 8. RTX 压缩的未来位置

RTX/Tool Result Compression 属于 Router 插件能力。首选在 Request Pipeline 内解析 Provider 请求结构、识别 Tool Result 并压缩，再交给 Credential Protection 等 Entry 顺序执行。只有当多个 Router 功能确实需要独立的结构化 Tool Result 生命周期时，才新增 Router `ToolResultPipeline`；不得把挂载点放入内置助手的 `AgentLoop`。

### 9. 错误隔离

普通 Extension 可 ContinueOnError；Credential Protection 的 request/response Extension 均为 FailClosed。请求 token 化或响应恢复失败时只返回安全摘要并终止原始数据继续流动。

## Risks / Trade-offs

- SQLite/DPAPI 损坏或当前用户上下文变化会导致 token 无法恢复；按 fail closed 处理，不降级猜测或泄露原文。
- token 长期有效意味着数据库需作为用户配置数据备份；数据库不含明文，但仍应限制访问。
- Prompt 可降低模型改写 placeholder 的概率，但不能提供确定性保证；任何实现都不得把 Prompt 遵循情况当成安全证明。
- 兼容解析模式增加产品和 Runtime 生命周期复杂度，但可避免普通“禁用”操作破坏历史会话，是长期 token 的必要兼容责任。
- 助手 Session 与历史文件不再由 Router 插件处理；如果产品要求防肩窥或历史文件打码，必须单独实现和测试助手隐私选项。
- Native Anthropic 网关发送链已统一到共享 Provider Pipeline；内置 Agent 仍缺少 Anthropic 原生协议 `IModelClient`，作为明确后续工作。

## Open Questions

- 规则文件热加载随 Hot Reload change 处理。
- token 撤销、轮换、加密备份与数据库迁移策略后续单独设计。
- Prompt 注入在 OpenAI/Anthropic/Ollama 等不同协议中的 developer/system 合成顺序与对外可观测格式，在实现 change 中细化；不得退化为 user message。
- Credential Protection 生命周期是扩展现有通用 Plugin Runtime 启停模型，还是引入受保护系统插件/常驻 Reference Runtime，在实现 change 中选择并补齐迁移方案。
- RTX 压缩进入 Request Pipeline 后，根据解析与排序需求评估是否值得新增 Router ToolResult 扩展点。
