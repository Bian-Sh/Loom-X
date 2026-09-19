# Comet Design Handoff

- Change: structured-config-assistant-decisions
- Phase: design
- Mode: compact
- Context hash: c817bb5814bab0e9fef8e3d5c6074ffe7d650102e7719638c1c79fcaa3a24313

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/structured-config-assistant-decisions/proposal.md

- Source: openspec/changes/structured-config-assistant-decisions/proposal.md
- Lines: 1-34
- SHA256: 9b50cc1a65d213ba4f265bb583405b5dad0ffd4642f50b63ce47fec9b261f13b

```md
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

```

## openspec/changes/structured-config-assistant-decisions/design.md

- Source: openspec/changes/structured-config-assistant-decisions/design.md
- Lines: 1-94
- SHA256: 353137ec414ba4dbb3df109d89a2d745fd962b482ea13a381a248399f4f24f57

[TRUNCATED]

```md
## Context

See `proposal.md` for the motivation and user-facing scope. 当前 LoomX 的 Assistant 工具以 `ToolDefinition` 注册，AgentLoop 已有工具风险与逐条审批桥接；桌面端已有 `AssistantViewModel`、`GlassDialogWindow` 和 Browser Bridge。项目尚无 TOML 依赖或通用文件修改服务。

本 Change 只建立通用基础能力。Codex Profile、Catalog Builder 和 `codex.configure` 由依赖 Change 实现；本 Change 不把 Codex 语义写入 TOML Engine 或 AskUser 核心。

## Goals / Non-Goals

**Goals:**

- 提供可复用的 TOML 文档解析、路径访问、结构化修改、验证、备份与原子写入。
- 在 TOML 写入失败时保护原文件，并让工具输出符合 LoomX SecretBoundary 约束。
- 将 Assistant 的用户决策建模为可暂停、可取消、可恢复的结构化请求。
- 复用现有桌面 Dialog 和 Assistant 生命周期，不引入 WebView 或第三方 Agent Runtime。
- 允许后续 Client Skill 通过 Browser Bridge 使用用户自己的 Chrome 获取公开资料，遇到网站挑战时交还用户。

**Non-Goals:**

- 不生成 Codex `model_catalog.json`。
- 不修改 Codex `config.toml`。
- 不管理或重启 Codex 进程，不实现 CDP 或 App-server 刷新。
- 不接入第三方搜索 API，不存储搜索 API Key，不实现反爬、验证码、登录墙或浏览器指纹绕过。
- 不要求 Router 在 Model 层声明或验证上游能力契约。

## Decisions

### 1. 使用语法树而不是反序列化后整文件重写

TOML Engine 使用 Tomlyn 2.10.1 的 syntax parser/document model，并启用 trivia 捕获。读取/验证使用同一解析路径；修改通过 key/value/table 节点和结构化 TOML 值完成。最终文本由语法树 writer 输出，尽量保留 comments、无关 section、未知字段和原有布局。普通 `TomlSerializer` POCO 往返不作为编辑路径，因为它会丢弃未知字段，不能满足用户文件编辑要求。

替代方案：字符串替换无法正确处理 TOML 类型和 nested table；反序列化到 `Dictionary` 后重新序列化无法可靠保留 comments 和用户格式；自研 parser 超出范围。

### 2. 路径采用分段数组并集中处理 TOML 值

工具 API 使用 `path: string[]`，不使用点号字符串作为唯一语法，避免 dotted key、数组表和 key 名包含点号时歧义。工具层将 JSON 值转换为受控 TOML value 节点，只接受 string、integer、number、boolean、array、object；不接受 secret 以外的任意对象扩展。

### 3. 写入采用事务式候选文档

所有 set/delete/patch 先在内存文档副本上完成，执行结构校验和重新解析，再按以下顺序落盘：

```text
Read → Compare → Backup → Temp Write → Parse Temp → Atomic Replace → Parse Target
```

如果候选内容与现有内容相同，则返回 no-op，不创建备份。备份使用目标目录内带 UTC 时间戳的文件名；临时文件也在同一目录，以便 Windows 原子替换保持在同一卷内。替换失败时不删除可恢复的原文件或备份。

### 4. TOML 工具与 Assistant 工具分层

新增通用文件服务负责文档生命周期和结构化编辑；`toml.*` ToolDefinition 只负责 JSON Schema、参数解析、SecretBoundary 脱敏和安全摘要。Codex 或其他 Client 的高层工具不直接操作语法树，而是调用该服务。

工具风险分级：

- `toml.read/get/validate` 为 Read；
- `toml.set/patch` 为 Write；
- `toml.delete` 为 Destructive，并继续遵循现有 Assistant 权限策略。

### 5. AskUser 使用独立 Broker，不复用工具审批结果

工具审批回答的是“是否允许执行这个工具”；AskUser 回答的是一个包含字段结果的业务决策。两者都可以由 AssistantService 暴露给 ViewModel，但使用不同的请求类型和完成通道。

`UserDecisionBroker` 负责：

- 接收结构化请求；
- 为每个请求分配 request id；
- 暴露 pending request 事件给 AssistantViewModel；
- 使用 `TaskCompletionSource` 挂起工具 Handler；
- 在提交、取消、会话停止或 ViewModel 卸载时完成请求；
- 防止重复完成和永久等待。

Assistant 侧注册一个 `assistant.ask_user` 工具，使模型可以按需要发起请求；UI 不直接解析模型自然语言，而是渲染固定字段类型。

### 6. UI 采用现有 Dialog 和状态模型

新增 AskUser ViewModel/控件复用现有 `GlassDialogWindow` 风格和资源，不引入 WebView。单选、多选、数字和文本字段在一个 Dialog 中渲染；提交前校验必填字段；取消返回明确的 `cancelled` 结果。Assistant 页面关闭时由 broker 取消所有 pending request。

### 7. 资料检索遵循能力优先、用户浏览器兜底

本 Change 不新增搜索 Provider。后续 Skill 可以先让 Assistant 使用当前模型已提供的搜索能力；如果模型没有搜索工具，则调用已有 `browser.open/read/wait`，要求用户在 Chrome 中完成登录、验证码或 JS challenge。Browser Bridge 只读取用户已经授权的页面，不实现安全机制绕过。

## Risks / Trade-offs

```

Full source: openspec/changes/structured-config-assistant-decisions/design.md

## openspec/changes/structured-config-assistant-decisions/tasks.md

- Source: openspec/changes/structured-config-assistant-decisions/tasks.md
- Lines: 1-44
- SHA256: 49561b2884cffb463a6573c581b2808c86f5be5e6d3907973543d2d9358e8357

```md
## 1. 基础依赖与领域契约

- [ ] 1.1 在 `LoomX/LoomX.csproj` 与测试项目中加入 Tomlyn 2.10.1 依赖，并确认 `dotnet restore` 成功且没有改变现有配置数据库路径
- [ ] 1.2 定义 TOML 路径、受控值类型、读取/校验/写入结果和安全摘要契约，使用 `string[]` 表示路径，并以单元测试覆盖字符串、整数、浮点数、布尔值、数组和表值
- [ ] 1.3 定义统一的敏感键识别与脱敏策略，覆盖 `key`、`token`、`password`、`secret`、`authorization` 等路径，并以测试确认原始值不进入工具结果或日志

## 2. TOML 文档读取与结构化编辑

- [ ] 2.1 实现通用 TOML 文档服务的读取、路径查询和语法校验，统一使用 Tomlyn syntax parser/document model，并以测试覆盖嵌套表、数组表、dotted key、包含点号的键名和非法 TOML
- [ ] 2.2 实现 `set`、`delete`、`patch` 的内存候选文档编辑，将 JSON 输入转换为受控 TOML 值，并以测试确认注释、无关 section、未知字段和原有语义得到保留
- [ ] 2.3 实现 Read → Compare → Backup → Temp Write → Parse Temp → Atomic Replace → Parse Target 的事务式写入流程，临时文件与备份文件放在目标目录，并以测试确认 no-op 不创建备份
- [ ] 2.4 为 Windows 路径、空格、非 ASCII 字符、文件占用和替换失败增加安全处理与有限重试，并以失败回滚测试确认原文件内容保持不变且错误结果包含可恢复信息
- [ ] 2.5 为 TOML 服务补齐 `ILogger<T>` 结构化日志，记录操作类型、路径安全摘要、结果、错误类型和耗时，测试确认不记录完整文档、Secret、请求正文或响应正文

## 3. TOML Assistant 工具

- [ ] 3.1 注册 `toml.read`、`toml.get`、`toml.validate`、`toml.set`、`toml.patch` 和 `toml.delete` 的 `ToolDefinition` 与 JSON Schema，并以工具注册测试确认名称、参数和返回结构稳定
- [ ] 3.2 将 TOML 工具映射到既有风险等级：读取/查询/校验为 Read，设置/补丁为 Write，删除为 Destructive，并复用既有审批与取消机制完成权限测试
- [ ] 3.3 在工具边界应用路径校验、输入大小限制、敏感字段脱敏和安全错误摘要，并以测试确认异常输入不会写入目标文件或泄露敏感值

## 4. 结构化 AskUser Broker

- [ ] 4.1 定义 AskUser 请求、字段、选项、默认值、必填标记、影响摘要、取消状态和结构化结果模型，支持单选、多选、数字与自由文本，并以序列化/校验测试覆盖字段 id 冲突与非法选项
- [ ] 4.2 实现 `UserDecisionBroker` 的 request id 分配、pending 事件、`TaskCompletionSource` 等待、提交、取消、超时/会话取消和重复完成保护，并以并发与生命周期测试确认不会永久阻塞
- [ ] 4.3 注册 `assistant.ask_user` 工具并接入 AssistantService/AgentLoop，使工具调用可以暂停当前步骤、保留会话上下文并在用户提交后恢复，以集成测试验证提交、取消和页面关闭路径
- [ ] 4.4 在 AskUser 边界过滤 API Key、Authorization、完整请求正文和其他敏感内容，并以安全测试确认问题文本、选项、影响摘要和结果不会泄露敏感信息

## 5. AskUser 桌面端交互

- [ ] 5.1 在 AssistantViewModel 中订阅 Broker 的 pending request、提交、取消和页面卸载事件，维护当前会话状态，并以 ViewModel 测试确认关闭页面会取消所有等待请求
- [ ] 5.2 基于 `GlassDialogWindow` 风格实现 AskUser Dialog/ViewModel，渲染单选、多选、数字和自由文本字段，支持必填校验、默认值展示、提交和取消，并以 UI/视图模型测试覆盖各种字段组合
- [ ] 5.3 将 AskUser 的用户可见反馈接入 `ToastService`，区分提交成功、取消和错误状态，确认 Toast 不包含 Secret、Authorization、完整请求/响应正文或用户敏感输入

## 6. 资料收集与后续 Skill 约束

- [ ] 6.1 为后续 Client Skill 提供资料收集服务边界说明：优先使用 Assistant 模型已有搜索能力，其次使用现有 Browser Bridge，最后通过 AskUser 请求用户提供资料，并以文档测试确认流程不新增搜索 API Key
- [ ] 6.2 更新相关 Assistant/Browser 文档或 Skill 说明，明确登录、验证码、Cloudflare、JS challenge 等情况交还用户处理，禁止绕过网站安全机制，并确认本 Change 不引入 WebView、爬虫或第三方搜索 Provider

## 7. 验证与交付

- [ ] 7.1 编写并运行 TOML 服务、原子写入、敏感信息保护和失败回滚单元测试，确认新增测试全部通过
- [ ] 7.2 编写并运行 AskUser Broker、Assistant 工具接入、取消恢复和桌面端 ViewModel 的集成测试，确认提交、取消、页面关闭和会话停止均可收敛
- [ ] 7.3 运行 `dotnet test` 覆盖 `LoomX.Tests` 与现有测试，修复回归后确认日志、数据库路径和既有 Assistant 工具行为不变
- [ ] 7.4 运行 `openspec status --change structured-config-assistant-decisions --json`、`openspec validate structured-config-assistant-decisions --strict`，并检查任务勾选状态、变更范围和中文文档完整性

```

## openspec/changes/structured-config-assistant-decisions/specs/assistant-user-decisions/spec.md

- Source: openspec/changes/structured-config-assistant-decisions/specs/assistant-user-decisions/spec.md
- Lines: 1-57
- SHA256: 7d14b2908edd62ae959b79ea7a8576049595436b01dcf066070cc11429154d2e

```md
## Purpose

为 AI 助手提供统一的用户决策交互，使 Profile 构建、重启提醒和资料歧义处理可以一次性收集单选、多选、数值与文本输入，而不是依赖自然语言猜测。

## ADDED Requirements

### Requirement: Assistant 必须支持结构化用户决策请求
系统 SHALL 支持由助手发起包含标题、问题、字段、选项、默认值、必填标记和可取消状态的结构化 AskUser 请求，并 SHALL 支持单选、多选、数字输入和自由文本字段。

#### Scenario: 发起组合决策
- **WHEN** 助手需要同时确认上下文窗口、reasoning levels 和补充说明
- **THEN** 系统可以在一个 AskUser 请求中展示对应的单选、多选和文本输入字段

#### Scenario: 用户取消决策
- **WHEN** 用户关闭或取消 AskUser Dialog
- **THEN** 助手收到结构化取消结果，不得把取消解释为用户同意任何默认值

### Requirement: AskUser 必须暂停并恢复当前助手执行
系统 SHALL 在等待用户输入期间暂停当前工具调用或 Agent 步骤，保留请求标识和会话上下文，并在用户提交后恢复原流程。

#### Scenario: 用户提交选择
- **WHEN** 用户完成所有必填字段并提交
- **THEN** 系统按字段 id 返回结构化结果，助手可以继续执行原操作

#### Scenario: Assistant 运行中关闭页面
- **WHEN** AskUser Dialog 所属页面被关闭或会话被取消
- **THEN** 等待中的请求收到取消/失败结果，不能永久阻塞 Agent Loop

### Requirement: AskUser 必须控制询问时机和信息边界
系统 SHALL 允许调用方说明询问原因和影响摘要，且 SHALL 不要求用户在每个普通读写步骤中确认；AskUser 展示内容不得包含 API Key、Authorization、完整请求正文或其他敏感数据。

#### Scenario: 高影响配置决策
- **WHEN** Catalog Profile 缺少关键字段或配置变化需要用户决定是否稍后重启 Codex
- **THEN** 助手可以发起 AskUser，并展示安全摘要和可选行动

#### Scenario: 普通内部步骤
- **WHEN** 助手只是读取状态、验证 TOML 或执行已确认的非破坏性 Patch
- **THEN** 系统不强制额外弹出 AskUser

### Requirement: 资料收集必须优先复用已有能力且不绕过网站安全机制
系统 SHALL 优先使用当前 Assistant 模型已经提供的搜索或资料能力；不可用时 SHALL 允许通过现有 Chrome Extension/Browser Bridge 读取用户明确打开并授权的页面；系统 MUST NOT 实现第三方搜索 API Key、验证码绕过、登录墙绕过、Cloudflare/JS challenge 绕过、Cookie 注入、TLS fingerprint 或浏览器指纹伪装。

#### Scenario: 模型具备搜索能力
- **WHEN** 当前 Assistant 模型提供可用的官方搜索能力
- **THEN** 助手优先使用该能力获取资料，不要求用户配置新的搜索 API Key

#### Scenario: 模型没有搜索能力但浏览器已连接
- **WHEN** Assistant 没有搜索能力且 Chrome Extension 已连接
- **THEN** 助手可以打开官方文档或 Provider 页面，并读取用户授权的页面内容

#### Scenario: 页面需要用户处理障碍
- **WHEN** 页面出现登录、验证码、Cloudflare 或 JS challenge
- **THEN** 助手暂停并提示用户自行处理，处理完成后再继续读取，不尝试绕过页面安全机制

#### Scenario: 没有任何资料通道
- **WHEN** Assistant 没有搜索能力、Browser Bridge 未连接且用户未提供资料
- **THEN** 助手明确说明无法验证资料，并通过 AskUser 请求用户提供结论或文档内容

```

## openspec/changes/structured-config-assistant-decisions/specs/structured-toml-editing/spec.md

- Source: openspec/changes/structured-config-assistant-decisions/specs/structured-toml-editing/spec.md
- Lines: 1-57
- SHA256: ea72128cd9944ff0b5b6c1821b1c97b5fc72a25258c5b8354d11d8d6354b96ff

```md
## Purpose

为桌面 AI 助手提供可靠、可审计且与客户端无关的 TOML 结构化读写能力，避免使用 Shell、字符串替换或整文件重建破坏用户现有配置。

## ADDED Requirements

### Requirement: TOML 文件必须支持结构化读取与路径访问
系统 SHALL 提供对指定 TOML 文件的读取、合法性验证和 nested path 查询能力，路径访问 SHALL 能区分不存在路径与值为 null 的情况，并 SHALL 支持字符串、整数、浮点、布尔、数组和表等 TOML 类型。

#### Scenario: 读取嵌套配置
- **WHEN** 调用方读取包含 `[model_providers.loomx]` 的 TOML 文件并查询 `model_providers.loomx.base_url`
- **THEN** 系统返回该字符串值及其 TOML 类型，不需要调用方解析原始文本

#### Scenario: 验证非法 TOML
- **WHEN** 调用方验证包含未闭合字符串或非法表结构的 TOML 文件
- **THEN** 系统返回失败结果、可定位的解析错误，并且不修改该文件

### Requirement: TOML 修改必须使用结构化 Patch
系统 SHALL 提供 set、delete 和批量 patch 操作，支持 nested path，并 SHALL 只修改目标字段而保留无关 section、未知字段、注释和原文件中可保留的格式信息。

#### Scenario: 修改嵌套字段并保留无关配置
- **WHEN** 对已有 TOML 设置 `model` 和 `model_providers.loomx.base_url`
- **THEN** 目标字段被更新，无关字段和无关 section 保持可解析且语义不变

#### Scenario: 删除指定字段
- **WHEN** 删除一个存在的 nested path
- **THEN** 仅删除该 path，父表和其他兄弟字段仍然存在

#### Scenario: 批量 Patch 原子应用
- **WHEN** 批量 Patch 中任一操作的路径或值类型非法
- **THEN** 整批操作失败，原文件内容保持不变

### Requirement: TOML 写入必须遵循备份、验证和原子替换
系统 SHALL 在修改既有文件前创建可定位的备份，在写入前验证候选文档，在同目录完成临时文件写入与原子替换，并在写入后再次验证；写入失败不得留下半写入的目标文件。

#### Scenario: 成功修改既有文件
- **WHEN** 对存在的 TOML 文件执行有效 Patch
- **THEN** 系统先保存备份，再原子替换原文件，并返回备份路径、写入路径和验证成功状态

#### Scenario: Windows 路径包含空格
- **WHEN** TOML 文件路径或生成文件路径包含空格和非 ASCII 字符
- **THEN** 系统按文件系统路径处理，不依赖 Shell 转义，且能完成读取、备份、写入和验证

#### Scenario: 写入阶段失败
- **WHEN** 临时文件写入、验证或替换阶段发生失败
- **THEN** 系统保留原文件内容，返回失败原因，并不报告修改成功

### Requirement: TOML 工具输出和日志必须保护敏感信息
系统 SHALL 对包含 key、token、password、secret、authorization 等敏感路径的读取结果进行脱敏，且 SHALL 不记录完整 TOML 文档、请求正文或敏感值。

#### Scenario: 读取敏感配置
- **WHEN** TOML 中包含 `env_key`、`api_key`、`token` 或 `password` 字段
- **THEN** 工具输出只返回安全摘要或脱敏占位符，原始敏感值不进入助手上下文

#### Scenario: 操作失败日志
- **WHEN** TOML 解析或写入失败
- **THEN** 日志包含操作类型、路径安全摘要和错误类型，但不包含文件完整内容或敏感值

```
