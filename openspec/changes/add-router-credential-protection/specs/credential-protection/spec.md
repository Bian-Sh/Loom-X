## Purpose

定义 Credential Protection 插件的敏感数据防护能力，在数据进入 LLM 上下文、持久化或日志边界前完成凭据检测与脱敏，确保 API Key 等敏感信息不以明文形式越过安全边界。

## ADDED Requirements

### Requirement: 敏感数据检测

Credential Protection SHALL 在数据进入后续组件前检测其中的敏感内容，覆盖常见 API Key 形态（如 sk- 前缀密钥）、Bearer Token、JWT 形态以及按名称识别的敏感字段（如 api_key、authorization、token、secret、自定义 Header 值）。

#### Scenario: 检测出明文 API Key

- **WHEN** 待处理数据中含有 `sk-` 前缀的密钥或 `Bearer ` 形式的凭据
- **THEN** 该内容被识别为敏感数据并进入脱敏处理

#### Scenario: 普通业务数据不误判

- **WHEN** 待处理数据仅为模型名称、Provider 标识、状态码等非敏感内容
- **THEN** 数据原样通过，不被标记为敏感

### Requirement: 敏感规则管理

敏感规则 SHALL 作为 Plugin-owned 数据由 Credential Protection 独立管理，支持规则的启用、禁用与增删。规则的修改 MUST NOT 要求修改宿主核心配置或其他插件的数据。

#### Scenario: 新增自定义敏感规则生效

- **WHEN** 用户为插件新增一条自定义敏感名称规则并保存
- **THEN** 后续流经的数据按新规则执行检测与脱敏

### Requirement: 脱敏替换

被判定为敏感的数据值 SHALL 在输出前替换为固定占位符，替换结果 MUST NOT 包含原始敏感值的任何片段，也不得包含可逆推原始值的信息。

#### Scenario: 敏感值被占位符替换

- **WHEN** 待处理数据中含有明文 API Key
- **THEN** 输出中该值被替换为固定占位符，且不包含原始密钥的任何字符

### Requirement: 持久化前清理

在数据进入 Recall、日志或持久化组件之前，Credential Protection SHALL 执行持久化清理（Persistence Sanitization）。任何 Recall / Log / Persistence 组件 MUST NOT 在脱敏之前保存原始敏感数据。

#### Scenario: 持久化内容不含敏感形态

- **WHEN** 一条含有敏感数据的 Tool Result 即将写入会话历史或日志
- **THEN** 写入内容已完成脱敏，不含 sk- 密钥或 Bearer Token 形态

### Requirement: 脱敏失败时 fail closed

检测或替换过程发生失败时，Credential Protection MUST 阻止原始数据进入后续组件，返回安全失败结果，不得默认放行未处理的原始数据。

#### Scenario: 脱敏处理失败不放行原始数据

- **WHEN** 敏感数据处理过程中发生不可恢复的内部失败
- **THEN** 原始数据被阻止进入后续组件，调用方收到安全失败结果