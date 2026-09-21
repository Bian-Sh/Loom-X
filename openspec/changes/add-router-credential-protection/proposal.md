## Why

LoomX Router 涉及 API Key 的读写与转发，数据在推送外部 AI、持久化会话或写日志之前缺少统一的脱敏边界，敏感数据可能进入 LLM 上下文、历史文件或日志造成泄露。`.design/LoomX_Plugin_System_Design_CN.md` 已明确：数据脱敏属于 Router Plugin Pipeline 能力，应以第一方 Credential Protection 插件落地，并在正式修改 LoomX 前先通过 PluginPlayground 验证 Runtime/Pipeline。

## What Changes

- 新增最小契约程序集 `LoomX.Plugin.Abstractions`（Plugin Manifest、Extension 接口、Pipeline 上下文），不依赖 LoomX UI 与 Router 内部实现。
- 新增 Plugin Runtime：从目录发现插件、验证 Manifest、经 AssemblyLoadContext 动态加载、注册 Router Extension、同一 Pipeline 内按配置顺序执行、插件异常隔离（数据安全类 Extension 失败 fail closed，不放行原始数据）。
- 在现有 Router 数据流中确定并挂载 Pipeline 扩展点（Provider 执行管道、助手 Tool Result 进入上下文边界、会话持久化边界；具体挂载点以源码验证为准）。
- 新增第一方 Credential Protection 插件：Credential Detection、Sensitive Rule（Plugin-owned 配置数据）、Mask/Placeholder、Persistence Sanitization。
- 新增 `LoomX.PluginPlayground` 验证项目，聚焦验证 Runtime/Pipeline 与 Sensitive Data 插件的组合（设计文档第 22 节 Phase 1 与 Phase 3 聚焦部分）。
- 保持单 change 不拆分：范围确认时已选择"脱敏优先"，Playground 验证与 Credential Protection 落地是同一连贯能力的顺序里程碑，拆分反而割裂验证与落地的验收闭环。
- 非目标：Settings UI / SettingsProvider 与 Avalonia 动态 XAML、Hot Reload、Tool Result Compression 插件、Plugin Marketplace / 在线仓库 / 签名体系 / 跨进程沙箱、插件间依赖图与全局 Priority DSL。

## Capabilities

### New Capabilities

- `plugin-runtime-pipeline`: 定义插件目录发现、Manifest 契约与验证、AssemblyLoadContext 动态加载、Extension 注册、同一 Pipeline 内 Entry 有序执行、启用/禁用以及插件异常隔离的行为。
- `credential-protection`: 定义敏感数据检测（Credential Detection）、敏感规则管理（Plugin-owned Sensitive Rule）、脱敏替换（Mask/Placeholder）、持久化清理（Persistence Sanitization）以及脱敏失败时 fail closed 的安全行为。

### Modified Capabilities

无。

## Impact

- 新增 `LoomX.Plugin.Abstractions`、宿主侧 Plugin Runtime、`LoomX.PluginPlayground`、第一方 Credential Protection 插件及对应测试项目。
- 修改 `LoomX`（Router Pipeline 挂载点与 DI 注册）；助手 Tool Result 与会话持久化边界的挂载以源码验证为准，可能涉及 `LoomX.Harness`。
- 不新增第三方 NuGet 依赖；AssemblyLoadContext 为 .NET 内置能力。
- 安全约束：任何 Recall / Log / Persistence / 外部模型调用不得在脱敏前接触并保存原始敏感数据；数据安全类 Extension 失败时不得静默放行原始数据。
- 既有助手侧保护（`SecretBoundary`、`SensitiveKeyPolicy`、`ToolArgumentSafety`、`AssistantSessionStore.SecretLeakScan`）保留并继续生效，本 change 不以删除或替代它们为目标。