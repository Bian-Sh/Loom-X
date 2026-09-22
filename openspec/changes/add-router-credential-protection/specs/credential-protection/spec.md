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

### Requirement: Placeholder 完整性指令

Credential Protection SHALL 将 placeholder 完整性指令作为协议必选组成部分。Request Pipeline 完成 token 化后，若最终 Provider 请求正文含 LoomX placeholder，系统 MUST 在发送 Provider 前临时注入不可单独关闭的 system/developer 级完整性指令，要求模型在 JSON、Header、URL、Shell 或 Tool Call 外围语法中逐字符保留完整引用。该指令 MUST NOT 包含凭据明文、token 映射或 Vault 内部信息，且 MUST NOT 改写客户端原始 system message 或会话持久化内容。

#### Scenario: 含 placeholder 的请求强制注入完整性指令

- **WHEN** Request Pipeline 处理后的 Provider 请求正文含本地签发的 LoomX placeholder
- **THEN** Provider 请求附带 placeholder 不可修改、翻译、拆分、转义或重新格式化的完整性指令

#### Scenario: 不含 placeholder 的请求不额外注入

- **WHEN** Request Pipeline 处理后的 Provider 请求正文不含 LoomX placeholder
- **THEN** Credential Protection 不为该请求额外注入 placeholder 完整性指令

#### Scenario: 用户知悉系统指令修改

- **WHEN** 用户查看或启用 Credential Protection
- **THEN** 产品界面明确披露该插件会在含 placeholder 的请求中修改发送给 Provider 的 system/developer 指令，且说明 Prompt 只降低模型改写概率、不构成安全保证

### Requirement: Placeholder 受限归一化

Credential Protection SHALL 只对已定位的 placeholder 候选执行 ASCII 大小写折叠与 token 语法内部允许的 ASCII 空白归一化。系统 MUST NOT 对整段正文全局删空白，不得使用 Unicode 相似字符替换、易混字符替换、缺字补全、编辑距离或其他模糊匹配。归一化结果 MUST 完整满足固定 token 语法并精确命中本地 SQLite 中唯一映射，否则不得恢复。

#### Scenario: 大小写和内部空白变化仍可恢复

- **WHEN** Provider 返回的本地签发 placeholder 仅发生 ASCII 大小写变化或 token 语法内部允许的 ASCII 空白变化
- **THEN** 系统将候选归一化为规范 token，精确查表后恢复原值

#### Scenario: 字符损坏不进行模糊猜测

- **WHEN** placeholder 出现缺字、易混字符替换、Unicode 相似字符或可对应多个 token 的歧义
- **THEN** 系统不得根据相似度猜测凭据，并按所在安全边界保持未知引用或 fail closed

### Requirement: Placeholder 外围语法保持不变

Credential Protection SHALL 只规范和替换 placeholder 自身跨度，不得删除或重写外围引号、反引号、Header 前缀、URL、Shell 语法或其他字符。JSON 结构引号 MUST 由 JSON 解析与重新序列化管理；JSON 解析后仍属于字段值的引号 SHALL 被视为实际数据，并由 Tool Schema、Tool Executor 或目标协议判断是否合法。

#### Scenario: Shell 命令中的合法引号被保留

- **WHEN** Tool Call 的 command 字段在 Shell 引号内包含 LoomX placeholder
- **THEN** 系统只恢复 placeholder，外围 Shell 引号和命令结构保持不变

#### Scenario: 强类型字段的额外包装不被擅自删除

- **WHEN** JSON 解析后的纯凭据字段值包含 placeholder 之外的额外引号或反引号
- **THEN** Credential Protection 保留这些实际数据，由字段校验拒绝或接受，不自行修复工具调用语义

### Requirement: Provider 响应本地恢复

Credential Protection SHALL 在成功的 Provider 响应返回 Router 客户前恢复本地签发的有效占位符。普通 JSON 响应 MUST 保持有效 JSON；SSE 流式响应 MUST 能恢复被拆分到多个网络 chunk 或内容事件中的占位符，并按独立内容通道隔离缓冲。流式候选长度保护 MUST 只计算实际未闭合 placeholder，不得把候选之前的普通文本计入 token 长度。

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

Credential Protection SHALL 只挂载在 Router 统一 Provider request/response 边界。内置 AI 助手与外部 Agent Client SHALL 作为 Router 客户复用该边界；`AgentLoop`、`AgentSession`、Assistant UI 与会话持久化不得依赖 Plugin Runtime。

#### Scenario: 内置 AI 助手获得脱敏收益

- **WHEN** 内置 AI 助手构造的模型请求正文包含来自 Tool Result 或对话历史的明文凭据
- **THEN** 请求经共享的 Router Request Pipeline 脱敏后才发送给外部 Provider

#### Scenario: 外部网关客户获得相同保护

- **WHEN** 外部 Agent Client 通过 LoomX 网关发送包含明文凭据的请求正文
- **THEN** 同一个 Router Request Pipeline 在转发 Provider 前完成脱敏

#### Scenario: Tool Result 随下一轮请求统一脱敏

- **WHEN** Browser 或其他工具返回包含明文凭据的结果，且 Agent 客户端把该结果放入下一次 Provider 请求正文
- **THEN** Router Request Pipeline 在外发前统一 token 化该正文，不要求 Router 插件改写客户端 Session 或 UI

#### Scenario: 客户端本地展示不属于 Router 承诺

- **WHEN** Agent 客户端把含凭据的消息显示在自身 UI、日志或历史存储中
- **THEN** 该客户端自行负责本地隐私策略，Credential Protection 不声明能够控制或改写客户端展示

### Requirement: 历史 placeholder 生命周期兼容

Credential Protection SHALL 将“为新明文创建 token 的主动保护”和“解析既有 placeholder 的兼容运行时”作为不同生命周期。暂停主动保护时 MUST 停止新明文检测与 token 化，但 MUST 继续识别、归一化、注入完整性指令并恢复历史 placeholder。插件代码卸载与 Credential Vault 销毁 MUST 是两个独立操作。

#### Scenario: 暂停主动保护仍可继续历史会话

- **WHEN** 用户暂停对新敏感内容的保护后继续一个包含本地签发 placeholder 的历史会话
- **THEN** 既有 placeholder 仍经过完整性指令、受限归一化和 Response 恢复，不因普通禁用操作失效

#### Scenario: 暂停保护警告明文风险

- **WHEN** 用户准备暂停主动保护
- **THEN** 产品明确警告新请求及历史会话中保存的明文凭据可能直接发送给 Provider，且要求用户确认

#### Scenario: 卸载前警告历史引用失效

- **WHEN** 用户请求卸载 Credential Protection Runtime，尤其是 Vault 中仍存在 token 映射时
- **THEN** 产品通过受保护的危险操作流程强警告历史会话、外部 Agent 缓存、导出文件和备份中的 placeholder 将不可解析，并要求二次确认

#### Scenario: 卸载默认保留 Vault

- **WHEN** 用户确认卸载插件代码但未单独确认销毁 Credential Vault
- **THEN** Vault 与 token 映射被保留，安装兼容版本后历史 placeholder 可再次恢复

#### Scenario: 销毁 Vault 使用独立不可逆确认

- **WHEN** 用户请求销毁 Credential Vault
- **THEN** 系统将其作为独立于卸载的不可逆高风险操作，明确说明所有历史 placeholder 将永久失效，并要求更高级别确认

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
