## Why

LoomX Plugin System 的定位是扩展 Router Provider 执行核心，而不是扩展内置 AI 助手的会话与 UI。当前 Router 在把请求正文发送给外部 Provider 前缺少统一的插件处理边界，API Key、Bearer Token 等误入用户消息、Tool Result 或其他请求内容时，可能随模型请求泄露给外部 AI。外部 Agent Client 经对外 Server 进入该执行核心；内置 AI 助手则绕过对外接入层，直接依赖 Plugin Runtime 装配的同一 Provider Pipeline。

## What Changes

- 新增最小契约程序集 `LoomX.Plugin.Abstractions`：Plugin Manifest、Router Request/Response Extension、Pipeline 上下文与结果，不依赖 LoomX UI、AI 助手或 Router 内部实现。
- 新增 Plugin Runtime：从目录发现插件、验证 Manifest、经 AssemblyLoadContext 动态加载、注册 Router Extension、同一 Pipeline 内按配置顺序执行、插件异常隔离。
- 在 Router 统一 Provider 执行链挂载 Request Pipeline：完整 `HttpRequestMessage` 构造完成后、`HttpClient.SendAsync` 之前处理请求正文。网关请求和内置 AI 助手请求均复用该边界。
- 在成功 Provider 响应返回 Router 客户前挂载 Response Pipeline，支持普通 JSON 与流式 SSE 恢复。
- 新增第一方 Credential Protection 插件：Credential Detection、Plugin-owned Sensitive Rule、SQLite 长期结构化 token、Provider Response Restore。用户消息与 Tool Result 等内容在组成 Provider Request 后统一 token 化。
- 将 placeholder 完整性 Prompt 定义为协议必选项：最终请求含 placeholder 时强制、临时且透明地注入 system/developer 指令；Prompt 只降低模型改写概率，确定性安全仍由受限归一化、SQLite 精确查表和 fail-closed 保证。
- 定义 placeholder 只做 ASCII 大小写与内部空白的受限归一化，且只替换 token 自身，不修改 JSON、Markdown、Header、URL、Shell 或 Tool Call 的外围语法。
- 将主动保护与历史兼容拆分生命周期：暂停保护只停止新明文 token 化，既有 placeholder 继续有效；卸载插件代码与销毁 Vault 分离，并对历史引用失效执行强警告和高风险确认。
- 新增 `LoomX.PluginPlayground` 验证 Runtime/Pipeline 与 Credential Protection 插件组合。
- 删除重复凭据实现：`SecretBoundary`、Browser Vault/Harvester 与 `SecretLeakScan`；`ToolCallProjection` 只负责工具协议投影，`AssistantContentPolicy` 只负责 TOML 暴露与 AskUser 产品校验。
- 非目标：对 `AgentSession`、Assistant UI 或会话 JSONL 进行插件脱敏；这些属于内置助手自身的数据与隐私策略。
- 非目标：本 change 新增 ToolResult Pipeline 或 RTX Compression。未来 RTX 压缩仍应实现于 Router，优先作为 Request Pipeline 的结构化处理阶段。

## Capabilities

### New Capabilities

- `plugin-runtime-pipeline`: 定义插件目录发现、Manifest 契约与验证、AssemblyLoadContext 动态加载、Router Request/Response Extension 注册、Pipeline 有序执行、启用/禁用以及异常隔离。
- `credential-protection`: 定义 Router 出站请求正文的敏感数据检测、规则管理、长期结构化 token，以及 Provider 响应本地恢复和失败时 fail closed。

### Modified Capabilities

无。

## Impact

- 新增 `LoomX.Plugin.Abstractions`、宿主侧 Plugin Runtime、`LoomX.PluginPlayground`、第一方 Credential Protection 插件及对应测试。
- 修改 Router Provider 执行管道与 DI 注册；不在 `AgentLoop`、`AgentSession` 或 `AssistantSessionStore` 挂载 Plugin Pipeline。
- 内置 AI 助手不经过对外 HTTP Server、Endpoint 鉴权或 Combo 路由，而是直接依赖 Plugin Runtime 装配的 `IProviderExecutionPipeline` 获得 request/response Credential Protection；外部 Agent Client 经对外接入层进入同一执行链。
- Credential token 映射新增 `Microsoft.Data.Sqlite` 与 DPAPI 依赖。
- 安全约束：请求正文未经数据安全 Pipeline 成功处理不得发送给外部模型；响应恢复失败不得静默放行未处理内容；任何实现不得把模型遵循完整性 Prompt 当成安全证明。
- 生命周期约束：普通“禁用”不得使历史 placeholder 失效；Credential Protection Runtime 卸载必须走强警告流程，Vault 默认保留，销毁 Vault 是独立不可逆操作。
- Provider 鉴权 Header 由 Router Core 管理，不纳入请求正文脱敏，避免破坏合法上游认证。
- Router 不承诺控制客户端 UI、日志、截图或本地会话文件。内置助手若需要 `***` 打码，应在自身“数据与隐私”能力中独立实现，可复用纯检测规则但不能把 Plugin Runtime 注入会话层。
