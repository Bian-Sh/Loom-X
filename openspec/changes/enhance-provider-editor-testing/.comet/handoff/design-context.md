# Comet Design Handoff

- Change: enhance-provider-editor-testing
- Phase: design
- Mode: compact
- Context hash: e8b698d83636770edd2e742df51433fa2627ffd97abc7ce4f436daea11466992

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/enhance-provider-editor-testing/proposal.md

- Source: openspec/changes/enhance-provider-editor-testing/proposal.md
- Lines: 1-30
- SHA256: f6acd44bf30eebc627a11607d234a1aab22e307ca79f2d9c61054a26d10fb04a

```md
## Why

当前 Provider 编辑器将接口协议和 OpenAI 请求格式拆成两个技术字段，Provider ID 需要用户手工输入，API Key 与常用基础信息分离；现有“测试连接”仅探测模型列表，无法验证真实模型推理、流式响应、代理与 CLI 身份模拟是否生效。需要将配置入口收敛为更直观的兼容类型，并提供可观察、可重试的轻量请求测试器。

## What Changes

- 基础 Tab 隐藏 Provider ID，为新 Provider 自动生成稳定且唯一的内部业务 ID，已有 Provider ID 保持不变。
- 将 Provider 类型与 OpenAI 请求格式合并为三个接口兼容类型：OpenAI Chat Completions、OpenAI Responses、Anthropic Messages。
- 将 API Key 从请求 Tab 移至基础 Tab，并保留安全显示/隐藏交互。
- 将请求 Tab 更名为高级，仅保留代理、自定义请求头和 CLI/UA 身份模拟配置。
- 移除高级 Tab 内旧的模型列表连接测试区块，保留页面顶部健康统计与“验证全部”能力。
- 新增测试 Tab，可选择模型、常规或流式请求模式及 Prompt，并默认使用“每日一言”。
- 新增真实 Provider 请求测试能力，按当前兼容类型、代理、API Key、自定义 Header 和 CLI 身份发送请求，并展示请求安全摘要、状态码、耗时、响应大小、响应内容与可重试错误。
- 测试过程支持取消、清空、复制响应和流式增量显示；切换 Provider 时取消未完成测试并重置上下文。
- 新增相关中英文资源、单元测试、视图契约测试和发布验证。

## Capabilities

### New Capabilities
- `provider-request-testing`: 定义 Provider 编辑器内真实模型请求测试器的输入、配置继承、普通/流式执行、响应展示及安全边界。

### Modified Capabilities
- `provider-panel`: 调整 Provider 详情 Tab 结构、自动内部 ID、兼容类型选择、API Key 位置及高级配置范围。

## Impact

- 影响 Avalonia Provider 页面、Provider 编辑与测试 ViewModel、本地化资源和相关 UI 契约测试。
- 新增独立 Provider 测试服务和统一测试请求/响应 DTO，并复用现有 Provider 发送管线和代理配置读取能力。
- 不修改数据库结构，不迁移或重写已有 Provider ID，不改变模型 Tab、Gateway 对外 API 或运行时数据库路径。
- 日志继续只记录 Provider、Model、协议、路径、状态码、字节数和耗时等安全摘要，不记录密钥、Header 值、Prompt 或响应正文。

```

## openspec/changes/enhance-provider-editor-testing/design.md

- Source: openspec/changes/enhance-provider-editor-testing/design.md
- Lines: 1-63
- SHA256: 684128a7f640f30ba49e1f67b2ec1e2300ef6a0c5a1280c60831c0dec5902ffd

```md
## Context

Provider 页面当前由 `ProvidersView.axaml` 与 `ProvidersViewModel`/`ProviderEditorViewModel` 共同承担配置、模型管理和健康检查。现有连接测试通过 `ProviderHealthService` 请求模型列表，只能验证模型目录端点；真实推理链路由协议客户端和 `ProviderExecutionPipeline` 处理。Provider 编辑器已支持 API Key、自定义 Header、代理开关和 CLI 身份 Header，但这些设置缺少统一的真实请求验证入口。

约束包括：不得改变数据库路径或静默创建第二份数据库；日志不得记录密钥、Header 值、Prompt 或响应正文；所有用户可见文案必须本地化；模型 Tab 行为保持不变；已有 Provider ID 不迁移。

## Goals / Non-Goals

**Goals:**

- 用一个兼容类型选项稳定映射现有 `ApiMode` 与 `EndpointFormat` 字段，避免数据库迁移。
- 将测试状态与 Provider 编辑状态解耦，避免继续把请求生命周期塞入主页面 ViewModel。
- 让测试请求尽量复用真实 Provider 请求的协议、Header、代理和响应收集逻辑。
- 普通与流式请求共享统一安全摘要和取消模型。
- 保持旧配置可读、可编辑和可回滚。

**Non-Goals:**

- 不实现完整 Postman 功能，不开放任意 URL、HTTP 方法、原始 Header 值编辑或任意 JSON 请求体。
- 不修改 Gateway 路由、模型数据结构、数据库 schema 或顶部批量健康检查语义。
- 不在本次变更中重写现有 Provider 健康检查服务。
- 不持久化测试 Prompt、响应正文或测试历史。

## Decisions

### 1. 兼容类型作为 ViewModel 映射层
新增不可持久化的兼容类型选项，将三个 UI 选项映射到现有字段组合：OpenAI Chat → `openai/chat_completions`，OpenAI Responses → `openai/responses`，Anthropic Messages → `anthropic`。保存时仍写入原字段，加载时按字段组合反向解析。这样无需数据库迁移，并保留运行时现有协议分支。替代方案是新增数据库枚举字段，但会重复表达相同状态并增加迁移风险。

### 2. Provider ID 在创建时生成且保持稳定
新建 Provider 时生成 `provider-` 加短随机十六进制后缀，并在当前 Provider 集合内检查冲突。ID 只作为内部业务键，不随显示名称变化。配置服务继续执行唯一性校验作为最终保护。替代方案是从显示名称生成 slug，但名称尚未输入且后续可变，会产生引用漂移和冲突。

### 3. 测试状态使用独立 ViewModel
新增 `ProviderTestPanelViewModel`，由 `ProvidersViewModel` 持有并在选中 Provider 变化时切换上下文。它负责模型选择、模式、Prompt、运行状态、响应文本、摘要和命令；Provider 编辑 ViewModel 只提供当前配置快照。这样可以隔离取消、流式追加和响应截断状态，并降低主 ViewModel 的耦合。

### 4. 新增专用测试服务并复用发送管线
新增 `IProviderTestService`/`ProviderTestService` 与协议无关的测试请求、进度和结果 DTO。服务按兼容类型构造 OpenAI Chat、OpenAI Responses 或 Anthropic Messages 请求，应用 API Key、自定义 Header 与 CLI 身份，并通过与真实 Provider 请求一致的执行管线发送。代理客户端由全局代理设置与 Provider `UseProxy` 共同决定，禁止仅在摘要中声称使用代理却仍通过固定直连 HttpClient 发送。

测试服务负责协议响应解析与流式事件归一化，ViewModel 只消费文本增量和最终元数据。替代方案是让 ViewModel 直接使用 HttpClient，但会重复协议实现、难以单测并容易泄露敏感字段。

### 5. 受控的响应展示
测试器允许在 UI 中显示响应正文，但普通和流式累计文本都设置字符上限；超过上限后停止向 UI 追加并标记截断，同时仍正确结束或取消底层请求。日志只记录安全元数据。复制操作仅复制用户主动可见的响应内容，并通过 `ToastService` 反馈。

### 6. UI 使用四 Tab 与终端式结果面板
基础 Tab 使用三张可选兼容卡片，包含名称、Base URL 和 API Key；高级 Tab 保留代理、自定义 Header 与 CLI 身份；模型 Tab 原样保留；测试 Tab 使用上方请求表单、中部配置摘要、下方深色响应面板。执行时发送按钮切换为停止，完成后提供重试、复制和清空操作。所有颜色使用现有动态资源，透明主题下不依据截图硬编码颜色。

## Risks / Trade-offs

- [不同兼容服务对请求/响应存在非标准扩展] → 只实现三种明确协议的最小标准载荷，错误时保留 HTTP 与安全响应摘要，不推断未知格式。
- [代理客户端创建不当导致连接泄漏] → 测试服务集中管理按代理配置创建的 HttpClient/Handler 生命周期，并确保取消令牌贯穿发送与读取。
- [流式响应高速追加导致 UI 卡顿] → 进度回调做节流或批量追加，并对总字符数设置上限。
- [自动保存尚未完成时点击测试] → 测试请求使用当前内存编辑快照，而非重新从数据库读取；配置合法性在发送前同步校验。
- [Anthropic 或 OpenAI 兼容服务要求特定 Header] → 使用协议标准鉴权 Header，并允许 Provider 自定义 Header 覆盖非受保护字段；Authorization/API Key 的安全优先级由服务统一控制。
- [旧 Ollama Provider 无法映射到三个新选项] → 已有 `ollama` 配置继续按兼容回退显示为 OpenAI Chat 只读映射风险较高，因此加载时保留内部旧值并明确回退到 OpenAI Chat；保存后转换为受支持组合。相关行为由测试固定。

## Migration Plan

1. 先引入映射与自动 ID 单元测试，再调整 Provider 编辑 UI。
2. 引入测试服务、协议请求测试和安全日志测试。
3. 接入独立测试 ViewModel 与测试 Tab，并补齐本地化和视图契约。
4. 运行定向测试、完整测试、构建和 UI 自动化验证。
5. 重新发布到带日期时间的 `outputs` 子目录。

回滚时可恢复旧 XAML 与 ViewModel 映射；数据库字段未改变，新增服务不持久化数据，因此无需数据回滚。

```

## openspec/changes/enhance-provider-editor-testing/tasks.md

- Source: openspec/changes/enhance-provider-editor-testing/tasks.md
- Lines: 1-32
- SHA256: d12f0e261e05260779ee279d8279c2b2fa56a24a0f5772166505980f9e49bf54

```md
## 1. Provider 基础配置模型

- [ ] 1.1 为兼容类型映射和旧配置反向解析编写失败测试，实现 OpenAI Chat、OpenAI Responses、Anthropic Messages 三种映射并验证测试通过
- [ ] 1.2 为新 Provider 稳定唯一业务 ID 编写失败测试，实现自动生成与冲突保护，并验证名称和类型变化不会修改 ID
- [ ] 1.3 调整 Provider 编辑持久化属性与加载行为，验证已有 Provider ID、API Key 和旧协议配置仍可无损读取保存

## 2. 真实请求测试服务

- [ ] 2.1 定义测试请求、进度、结果和错误 DTO，并用测试固定安全摘要、响应截断和取消语义
- [ ] 2.2 为 OpenAI Chat、OpenAI Responses 和 Anthropic Messages 编写失败测试，实现普通请求构造、鉴权、自定义 Header 与响应解析
- [ ] 2.3 为三种协议编写流式响应测试，实现文本增量归一化、完成状态、错误事件和长度限制
- [ ] 2.4 接入 Provider 代理设置与 CLI 身份 Header，验证代理开关真实影响 HttpClient 且日志不包含密钥、Header 值、Prompt 或响应正文

## 3. 测试面板状态与命令

- [ ] 3.1 新增独立 ProviderTestPanelViewModel，并验证模型默认选择、常规/流式模式、默认“每日一言”和无模型禁用状态
- [ ] 3.2 实现发送、停止、重试、清空和复制响应流程，验证切换 Provider 会取消旧请求并清空上下文
- [ ] 3.3 将测试面板接入 ProvidersViewModel 的选择和生命周期，验证自动保存中的内存配置可直接用于测试

## 4. Provider 页面与本地化

- [ ] 4.1 重构基础 Tab 为名称、三种兼容类型卡片、Base URL 和 API Key，隐藏 Provider ID，并通过视图契约测试验证
- [ ] 4.2 将请求 Tab 更名为高级，仅保留代理、自定义请求头与 CLI/UA 模拟，移除旧连接测试区块且保留顶部健康验证
- [ ] 4.3 新增专业测试 Tab 的请求表单、安全摘要和终端式 Response 面板，验证发送/停止/重试/复制/清空绑定与响应状态可见性
- [ ] 4.4 补齐 zh-CN、en-US 与 zh-TW 本地化资源，运行本地化覆盖和硬编码文案测试

## 5. 集成验证与交付

- [ ] 5.1 运行 Provider、测试服务、ViewModel、视图契约和敏感日志定向测试并修复失败
- [ ] 5.2 运行完整测试与 Release 构建，确认无编译错误、无新增警告回归且 OpenSpec 严格验证通过
- [ ] 5.3 使用 CUA 后台启动应用，验证四个 Tab、三种兼容选择、普通/流式测试、取消和错误展示
- [ ] 5.4 重新发布桌面应用到带可读日期时间的 `outputs` 子目录，验证发布包可启动且进程路径正确

```

## openspec/changes/enhance-provider-editor-testing/specs/provider-panel/spec.md

- Source: openspec/changes/enhance-provider-editor-testing/specs/provider-panel/spec.md
- Lines: 1-31
- SHA256: 278488ad8fc6270443eabaaa71536d9f5ca49e83cbeade1e0dbe2d018fcfb2cc

```md
## ADDED Requirements

### Requirement: Provider 基础配置使用稳定内部 ID 与统一兼容类型
Provider 面板 MUST 隐藏 Provider ID 输入，新建 Provider 时 MUST 自动生成唯一且稳定的内部业务 ID；已有 Provider 的业务 ID MUST 保持不变。基础 Tab MUST 将协议类型和 OpenAI 请求格式合并为 OpenAI Chat Completions、OpenAI Responses、Anthropic Messages 三个兼容类型，并将 API Key 与名称、Base URL 一同提供。

#### Scenario: 新建 Provider 自动获得内部 ID
- **WHEN** 用户新建 Provider 并修改名称或兼容类型
- **THEN** 系统自动生成不可见的唯一业务 ID，且该 ID 不随名称或兼容类型变化

#### Scenario: 已有 Provider 保持业务 ID
- **WHEN** 用户打开并保存升级前已存在的 Provider
- **THEN** 系统保留原业务 ID，不执行迁移、重命名或创建第二份 Provider

#### Scenario: 选择统一兼容类型
- **WHEN** 用户选择 OpenAI Chat Completions、OpenAI Responses 或 Anthropic Messages
- **THEN** 系统保存与该选项对应的协议和请求格式，并在重新打开页面后回显同一选项

### Requirement: Provider 详情区按基础、高级、模型和测试组织
Provider 详情区域 MUST 提供基础、高级、模型和测试四个 Tab。高级 Tab MUST 仅包含代理、自定义请求头和 CLI/UA 身份模拟，不得包含旧的连接测试区块；模型 Tab 的现有模型同步、搜索、启停、排序和删除行为 MUST 保持不变。

#### Scenario: 打开高级 Tab
- **WHEN** 用户打开 Provider 的高级 Tab
- **THEN** 页面只显示代理、自定义请求头和 CLI/UA 身份模拟配置，不显示旧的连接测试卡片

#### Scenario: 页面健康统计保持可用
- **WHEN** 用户移除高级 Tab 内的旧连接测试入口后查看 Provider 页面顶部
- **THEN** 健康统计、Provider 健康状态和“验证全部”入口仍然可用

#### Scenario: 模型 Tab 行为保持不变
- **WHEN** 用户打开模型 Tab
- **THEN** 原有模型同步、搜索、启停、拖放排序、元数据展示和删除能力继续工作

```

## openspec/changes/enhance-provider-editor-testing/specs/provider-request-testing/spec.md

- Source: openspec/changes/enhance-provider-editor-testing/specs/provider-request-testing/spec.md
- Lines: 1-61
- SHA256: 25b17e2ec3e9597beb8eb5dcef22e03abe6c7805a3278c78d29f09b58fecec8f

```md
## Purpose

为 Provider 配置提供一个轻量、可观察且安全的真实模型请求测试器，用于验证协议、模型、代理、自定义请求头和 CLI 身份模拟在普通或流式推理请求中的实际效果。

## ADDED Requirements

### Requirement: 用户可以配置并发送真实模型测试请求
测试 Tab MUST 允许用户从当前 Provider 中选择模型、选择常规或流式模式并编辑发送内容；发送内容默认 MUST 为“每日一言”。系统 MUST 按当前 Provider 兼容类型构造并发送真实推理请求。

#### Scenario: 发送常规请求
- **WHEN** 用户选择启用模型、保留默认 Prompt 并以常规模式发送
- **THEN** 系统使用当前 Provider 配置发送一次非流式推理请求，并展示最终响应

#### Scenario: 发送流式请求
- **WHEN** 用户选择流式模式并发送请求
- **THEN** 系统持续追加可显示的文本片段，并在流结束后展示完整结果和响应元数据

#### Scenario: 没有可用模型
- **WHEN** 当前 Provider 不存在可选择的真实模型
- **THEN** 发送操作不可用，页面明确提示先同步或添加模型

### Requirement: 测试请求继承当前 Provider 的有效连接配置
测试请求 MUST 使用当前 Provider 的 Base URL、API Key、自定义请求头、代理开关和已应用的 CLI/UA 身份。请求摘要 MUST 显示协议、路径、模型、模式、代理状态、CLI 身份与版本、自定义 Header 数量和请求 ID，但 MUST NOT 显示 API Key、Authorization 或 Header 值。

#### Scenario: 使用代理和 CLI 模拟
- **WHEN** Provider 已启用代理并应用 CLI 身份
- **THEN** 测试请求通过有效代理设置发送并携带对应 CLI 身份请求头，摘要显示代理与 CLI 安全信息

#### Scenario: 自定义请求头包含敏感值
- **WHEN** Provider 配置了自定义请求头
- **THEN** 测试请求携带完整 Header，但 UI 摘要和运行日志仅显示 Header 数量而不显示值

### Requirement: 测试器提供专业的执行状态与响应信息
测试 Tab MUST 展示准备、发送、连接、完成、取消或失败状态，并在可用时展示 HTTP 状态码、耗时、内容类型、响应字节数和响应正文。用户 MUST 可以停止进行中的请求、清空结果、复制响应和重试最近一次请求。

#### Scenario: 请求成功
- **WHEN** 上游返回成功响应
- **THEN** 页面显示完成状态、HTTP 状态码、耗时、响应大小和可复制的响应内容

#### Scenario: 请求失败
- **WHEN** 上游返回 401、404、429、5xx、无效协议响应或发生网络超时
- **THEN** 页面显示安全错误摘要和可用元数据，并提供重试操作

#### Scenario: 用户停止请求
- **WHEN** 用户在请求进行中点击停止
- **THEN** 当前请求被取消，页面显示已取消且不将取消视为未处理异常

#### Scenario: 切换 Provider
- **WHEN** 用户在测试请求进行中切换到另一个 Provider
- **THEN** 系统取消旧请求并清空旧 Provider 的测试上下文，避免响应串入新 Provider

### Requirement: 测试器保护敏感内容并控制展示成本
业务日志 MUST NOT 记录 API Key、Authorization、自定义 Header 值、Prompt、响应正文或流式片段。响应正文 MAY 在测试 Tab 中展示，但系统 MUST 对累计和渲染长度设置上限，避免异常上游响应导致 UI 无界增长。

#### Scenario: 记录测试请求日志
- **WHEN** 测试请求开始、完成、取消或失败
- **THEN** 日志只包含 Provider、Model、协议、路径、状态码、内容类型、字节数、代理状态和耗时等安全摘要

#### Scenario: 上游返回超长内容
- **WHEN** 普通或流式响应超过测试器展示上限
- **THEN** 页面保留受限长度内容并明确标记已截断，应用继续保持响应

```
