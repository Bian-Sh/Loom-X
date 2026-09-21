## Why

LoomX Plugin System 的定位是扩展 Router，而不是扩展内置 AI 助手。当前 Router 在把请求正文发送给外部 Provider 前缺少统一的插件处理边界，API Key、Bearer Token 等误入 prompt、Tool Result 或其他请求内容时，可能随模型请求泄露给外部 AI。`.design/LoomX_Plugin_System_Design_CN.md` 已明确：内置 AI 助手只是 Router 的消费者，应与外部 Agent Client 一样复用 Router Plugin Pipeline，从而自然获得脱敏以及后续 Tool Result Compression 等能力。

## What Changes

- 新增最小契约程序集 `LoomX.Plugin.Abstractions`（Plugin Manifest、Router Extension 接口、Pipeline 上下文），不依赖 LoomX UI、AI 助手或 Router 内部实现。
- 新增 Plugin Runtime：从目录发现插件、验证 Manifest、经 AssemblyLoadContext 动态加载、注册 Router Extension、同一 Pipeline 内按配置顺序执行、插件异常隔离（数据安全类 Extension 失败 fail closed，不放行原始数据）。
- 在 Router 的统一 Provider 执行链挂载 Request Pipeline：完整 `HttpRequestMessage` 构造完成后、`HttpClient.SendAsync` 之前处理请求正文。网关请求和内置 AI 助手请求均复用该边界。
- 新增第一方 Credential Protection 插件：Credential Detection、Sensitive Rule（Plugin-owned 配置数据）、Mask/Placeholder；敏感内容离开 Router 安全边界前完成脱敏。
- 新增 `LoomX.PluginPlayground` 验证项目，聚焦验证 Runtime/Pipeline 与 Sensitive Data 插件的组合（设计文档第 22 节 Phase 1 与 Phase 3 聚焦部分）。
- 保持单 change 不拆分：范围确认时已选择“脱敏优先”，Playground 验证与 Credential Protection 落地是同一连贯能力的顺序里程碑，拆分反而割裂验证与落地的验收闭环。
- 非目标：Settings UI / SettingsProvider 与 Avalonia 动态 XAML、Hot Reload、Tool Result Compression 插件、Response 流式处理、Plugin Marketplace / 在线仓库 / 签名体系 / 跨进程沙箱、插件间依赖图与全局 Priority DSL。

## Capabilities

### New Capabilities

- `plugin-runtime-pipeline`: 定义插件目录发现、Manifest 契约与验证、AssemblyLoadContext 动态加载、Router Extension 注册、同一 Pipeline 内 Entry 有序执行、启用/禁用以及插件异常隔离的行为。
- `credential-protection`: 定义 Router 出站请求正文的敏感数据检测（Credential Detection）、敏感规则管理（Plugin-owned Sensitive Rule）、脱敏替换（Mask/Placeholder）以及脱敏失败时 fail closed 的安全行为。

### Modified Capabilities

无。

## Impact

- 新增 `LoomX.Plugin.Abstractions`、宿主侧 Plugin Runtime、`LoomX.PluginPlayground`、第一方 Credential Protection 插件及对应测试项目。
- 修改 `LoomX` 的 Router Provider 执行管道与 DI 注册；不修改 `LoomX.Harness`，不在 `AgentLoop` 或 `AssistantSessionStore` 挂载 Router Plugin。
- 内置 AI 助手继续作为 Router 客户，通过共享的 `IProviderExecutionPipeline` 自动获得 Credential Protection 收益；后续 Tool Result Compression 也应遵循同一 Router-first 关系。
- 不新增第三方 NuGet 依赖；AssemblyLoadContext 为 .NET 内置能力。
- 安全约束：请求正文未经数据安全 Pipeline 成功处理不得发送给外部模型；数据安全类 Extension 失败时不得静默放行原始数据。
- Provider 鉴权 Header 由 Router Core 管理，不纳入请求正文脱敏，避免破坏合法上游认证。
- 既有助手侧保护（`SecretBoundary`、`SensitiveKeyPolicy`、`ToolArgumentSafety`、`AssistantSessionStore.SecretLeakScan`）保留并继续生效，但它们不是 Router Plugin 挂载点。
