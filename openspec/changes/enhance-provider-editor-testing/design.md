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

测试服务负责协议响应解析；普通请求向 ViewModel 返回解析后的模型文本，流式请求则按接收顺序转发所有 SSE `data:` payload：有效 JSON 压缩为保留 Unicode 的单行 JSONL，非 JSON 与 `[DONE]` 原样保留，`event:`/`data:` 前缀不进入展示。完成事件必须先写入 Response，再结束读取。最终端点必须由 `ProviderRouteEndpointResolver` 与真实 OpenAI/Anthropic 路由共用同一套拼接实现，测试模块不得单独归一化或修正 URL。若当前配置按真实路由会形成 `/v1/v1/` 等重复版本段，测试摘要和实际请求必须原样反映，并由测试服务与真实路由写入不含敏感信息的 Warning 日志。替代方案是让 ViewModel 直接使用 HttpClient，但会重复协议实现、难以单测并容易泄露敏感字段。

### 5. 受控的响应展示
测试器允许在 UI 中显示响应正文，但普通和流式累计文本都设置字符上限；超过上限后停止向 UI 追加并标记截断，同时仍正确结束或取消底层请求。非 2xx、无效 JSON 和协议结构错误保留受限长度的原始上游内容，JSON 只进行缩进格式化；流式解析失败保留此前已收到的 JSONL 与当前原始 payload；没有响应体的异常生成安全错误 JSON。日志只记录安全元数据。Response 使用只读可选择文本和系统原生复制能力；没有响应内容时显示本地化空态并隐藏文本框，避免空内容出现滚动条；产生结果后再显示文本框并按内容按需滚动。请求结束后仅当用户全选全部内容并按 Delete 或 Backspace 时清空，部分选择或请求执行中不清空。由于只读 TextBox 会在冒泡阶段消费删除键，页面在父级隧道路由阶段监听该快捷键并包含已处理事件，确保真实键盘交互能够触发清空。

### 6. UI 使用四 Tab 与终端式结果面板
基础 Tab 使用三张可选兼容卡片，包含名称、Base URL 和 API Key；高级 Tab 保留代理、自定义 Header 与 CLI 身份；模型 Tab 原样保留；测试 Tab 使用模型与模式选择、实时安全摘要、单行输入框和 Response 面板。测试模型列表由独立测试 ViewModel 根据模型 Tab 的启用状态实时投影，只展示已启用的真实模型；当前模型被取消启用时自动切换到其他已启用模型，没有已启用模型时清空选择、禁用发送并显示本地化提示。摘要只展示最终端点、代理、Header 数量与 CLI 身份，不重复 Provider、模型、模式或请求 ID。发送按钮嵌入输入框，执行时显示不可交互的动态等待状态；Response 文本框直接占满结果面板，页面不展示 Response 标题，也不提供停止、重试、复制或可见清空按钮，仅保留文本原生复制与全选删除清空。所有颜色使用现有动态资源，透明主题下不依据截图硬编码颜色。

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
