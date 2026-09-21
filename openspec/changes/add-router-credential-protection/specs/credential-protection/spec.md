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

### Requirement: 脱敏替换

被判定为敏感的数据值 SHALL 在请求发往外部 Provider 前替换为固定占位符，替换结果 MUST NOT 包含原始敏感值的任何片段，也不得包含可逆推原始值的信息。

#### Scenario: 敏感值被占位符替换

- **WHEN** 待发送请求正文中含有明文 API Key
- **THEN** 实际发送给上游的正文中该值被替换为固定占位符，且不包含原始密钥的任何字符

### Requirement: Router 客户共享保护

Credential Protection SHALL 挂载在 Router 统一 Provider 执行边界，而不是内置 AI 助手的 `AgentLoop` 或会话存储中。所有复用该 Router 边界的客户 MUST 获得相同保护。

#### Scenario: 内置 AI 助手获得脱敏收益

- **WHEN** 内置 AI 助手构造的模型请求正文包含来自 Tool Result 或对话历史的明文凭据
- **THEN** 请求经共享的 Router Request Pipeline 脱敏后才发送给外部 Provider

#### Scenario: 外部网关客户获得相同保护

- **WHEN** 外部 Agent Client 通过 LoomX 网关发送包含明文凭据的请求正文
- **THEN** 同一个 Router Request Pipeline 在转发 Provider 前完成脱敏

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
