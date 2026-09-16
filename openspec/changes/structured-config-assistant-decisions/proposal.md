## Why

LoomX 小助手目前只有面向工具风险的逐条审批，无法在构建 Client 配置或诊断建议时，以结构化方式一次性收集用户的单选、多选、数值和补充文本决策。同时，Codex 接入需要安全修改用户配置文件，但现有工具体系没有通用 TOML 结构化编辑、备份、校验和原子写入能力，容易迫使 Agent 退回到不可靠的字符串或 Shell 操作。

本 Change 先提供与 Client 无关的通用基础能力：结构化 TOML 文件操作、可验证的安全写入，以及 Assistant 的 AskUser 决策通道。它为后续 Codex Catalog/Profile 配置提供可靠底座，同时保留用户对关键模型能力和操作时机的最终决定权。

## What Changes

- 新增通用 TOML 结构化操作能力：读取、路径读取、设置、删除、批量 Patch 和 Validate。
- TOML 修改遵循 Read → Backup → Structured Patch → Validate → Atomic Write，并尽量保留注释、无关字段和未知配置。
- 对 Windows 路径、嵌套表、字符串、整数、浮点、布尔、数组和表提供结构化处理。
- 新增通用 AskUser 决策请求/响应模型，支持单选、多选、数字输入、自由文本和取消。
- 将 AskUser 接入现有 Assistant 会话与桌面 Dialog，供后续 Profile 构建和重启提示复用。
- 资料检索只复用 Assistant 模型已有能力或现有 Chrome Extension/Browser Bridge；不新增第三方搜索服务、不接入搜索 API Key、不实现反爬或登录绕过。
- 不实现 Codex Catalog、Codex 配置、Codex 进程重启或无感刷新；这些属于后续 Change。

## Capabilities

### New Capabilities

- `structured-toml-editing`: 提供通用 TOML 读取、查询、修改、删除、批量 Patch、验证、备份和原子写入能力。
- `assistant-user-decisions`: 提供 Assistant 可暂停等待用户结构化决策，并在桌面端显示可操作的 AskUser Dialog。

### Modified Capabilities

- 无。现有 Assistant Session 生命周期的实现会被复用，但本 Change 不改变其既有会话持久化语义。

## Impact

- 影响 `LoomX.Harness` 的 Assistant 事件/工具调用桥接，以及 `LoomX.Assistant` 的工具注册、会话服务和桌面 ViewModel/Dialog。
- 新增通用配置文件服务与 TOML 依赖；具体依赖选择以现有 .NET 依赖策略和保留注释/格式能力为准。
- 可能新增 `toml.*` Function 与 AskUser 内部协议，但不改变现有 `ToolDefinition` 的调用契约。
- 扩展现有 Browser Bridge 的资料收集使用说明，不增加绕过 Cloudflare、验证码、登录墙、Cookie 或指纹的能力。
- 后续 `codex-client-integration` 将依赖本 Change 提供的 TOML 和 AskUser 能力。
