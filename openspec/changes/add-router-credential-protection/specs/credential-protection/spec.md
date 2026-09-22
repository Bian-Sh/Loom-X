## Purpose

定义 Credential Protection Router 插件的敏感数据防护能力，在 Router 请求正文离开本地安全边界、发送给外部 Provider 前完成凭据检测与脱敏。内置 AI 助手和外部 Agent Client 均作为 Router 客户复用该能力。

## ADDED Requirements

### Requirement: 敏感数据检测

Credential Protection SHALL 在 Router 请求正文进入外部 Provider 前检测其中的敏感内容，覆盖常见 API Key 形态（如 sk- 前缀密钥）、Bearer Token、JWT 形态以及按名称识别的敏感字段（如 api_key、authorization、token、secret、自定义 Header 值）。

#### Scenario: 检测出明文 API Key

- **WHEN** 待发送请求正文中含有 `sk-` 前缀的密钥或 `Bearer ` 形式的凭据
- **THEN** 该内容被识别为敏感数据并进入脱敏处理

#### Scenario: 普通业务数据不误判

- **WHEN** 待发送请求正文仅包含模型名称、Provider 标识或普通对话内容
- **THEN** 数据原样通过，不被标记为敏感

### Requirement: 敏感规则管理

敏感规则 SHALL 作为 Plugin-owned 数据由 Credential Protection 独立管理，支持规则的启用、禁用与增删。规则的修改 MUST NOT 要求修改宿主核心配置或其他插件的数据。

#### Scenario: 新增自定义敏感规则生效

- **WHEN** 用户为插件新增一条自定义敏感名称规则并保存
- **THEN** 后续流经 Router Request Pipeline 的请求正文按新规则执行检测与脱敏

### Requirement: 可恢复的脱敏替换

被判定为敏感的数据值 SHALL 替换为随机结构化 token。token MUST NOT 包含原始敏感值的任何片段或可逆推信息；插件 SHALL 在插件自有 SQLite 中长期保存唯一映射，原值 MUST 使用当前用户 DPAPI 加密且不得以明文落库，以便跨会话、跨进程重启恢复。

#### Scenario: 敏感值被结构化占位符替换

- **WHEN** 待发送请求正文中含有明文 API Key
- **THEN** 实际发送给上游的正文中该值被替换为本次插件引擎签发的结构化占位符，且不包含原始密钥的任何字符

#### Scenario: 未知占位符不被恢复

- **WHEN** Provider 响应包含并非本地 SQLite 映射签发的占位符形态文本
- **THEN** 该文本原样保留，不被解析为本地凭据

### Requirement: Provider 响应本地恢复

Credential Protection SHALL 在成功的 Provider 响应返回 Router 客户前恢复本地签发的有效占位符。普通 JSON 响应 MUST 保持有效 JSON；SSE 流式响应 MUST 能恢复被拆分到多个内容事件中的占位符，并按独立内容通道隔离缓冲。

#### Scenario: 普通 JSON 响应恢复

- **WHEN** Provider 的 JSON 响应字符串字段中包含当前引擎签发的完整占位符
- **THEN** Router 返回前将其恢复为原值，且原值中的引号、反斜杠与换行不会破坏 JSON

#### Scenario: SSE content 跨事件恢复

- **WHEN** Provider 将一个占位符拆分到同一 choice 的多个 `delta.content` 事件
- **THEN** Router 暂存相关事件，待占位符完整后恢复并继续输出，不向客户暴露残缺 token

#### Scenario: SSE tool arguments 跨事件恢复

- **WHEN** Provider 将一个占位符拆分到同一 tool call 的多个 `function.arguments` 事件
- **THEN** Router 按该 tool call 通道完成恢复，且生成的 arguments 保持有效 JSON 字符串片段

### Requirement: Router 客户共享保护

Credential Protection SHALL 挂载在 Router 统一 Provider request/response 边界；内置 AI 助手 SHALL 通过通用 `tool-result` 与 `persistence` Pipeline 在工具结果进入 Session/UI 前及会话写盘前复用同一插件。Harness 只依赖处理委托或 Pipeline Contract，不得依赖具体插件实现。

#### Scenario: 内置 AI 助手获得脱敏收益

- **WHEN** 内置 AI 助手构造的模型请求正文包含来自 Tool Result 或对话历史的明文凭据
- **THEN** 请求经共享的 Router Request Pipeline 脱敏后才发送给外部 Provider

#### Scenario: 外部网关客户获得相同保护

- **WHEN** 外部 Agent Client 通过 LoomX 网关发送包含明文凭据的请求正文
- **THEN** 同一个 Router Request Pipeline 在转发 Provider 前完成脱敏

#### Scenario: Tool Result 进入会话前脱敏

- **WHEN** Browser 或其他工具返回包含明文凭据的结构化结果
- **THEN** 结果经 `tool-result` Pipeline token 化后才进入 Session、事件与 UI

#### Scenario: 历史会话跨重启恢复

- **WHEN** token 化后的会话 JSONL 在应用重启后被加载
- **THEN** `response` Pipeline 使用 SQLite 长期映射恢复原内容，且 JSONL 与 token 数据库均不包含明文凭据

### Requirement: Provider 鉴权不被破坏

Credential Protection SHALL 处理 Router 请求正文，不得修改 Router Core 为合法上游认证设置的 Authorization、API Key 或自定义 Header。

#### Scenario: 上游认证 Header 保持原值

- **WHEN** Router 使用 Provider API Key 构造上游认证 Header，同时请求正文含有误入的明文凭据
- **THEN** 请求正文中的凭据被脱敏，Router 管理的认证 Header 保持不变并用于上游鉴权

### Requirement: 脱敏失败时 fail closed

检测或替换过程发生失败时，Credential Protection MUST 阻止原始请求正文发送给外部 Provider，返回安全失败结果，不得默认放行未处理的原始数据。

#### Scenario: 脱敏处理失败不放行原始数据

- **WHEN** 敏感数据处理过程中发生不可恢复的内部失败
- **THEN** Router 终止本次 Provider 请求，原始正文未发送给上游，调用方收到安全失败
