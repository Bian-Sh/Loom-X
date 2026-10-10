spec: []
---

## ADDED Requirements

### Requirement: 凭据保护支持从界面停用

系统 SHALL 允许用户在插件页停用凭据保护插件，且停用 MUST NOT 关闭 placeholder 的解析与恢复能力。

#### Scenario: 停用后停止识别新明文

- **WHEN** 用户停用凭据保护后发送包含新明文凭据的请求
- **THEN** 请求正文不做检测与token 化，原样发出
- **AND** 不注入新的 placeholder

#### Scenario: 停用后仍恢复历史 placeholder

- **GIVEN** 停用前已签发 placeholder 并写入历史会话
- **WHEN** 用户停用凭据保护后模型回覆中出现该 placeholder
- **THEN** placeholder 仍被识别并还原为原值
- **AND** 还原不因停用而失效

#### Scenario: 停用后仍对既有 placeholder 注入完整性指令

- **GIVEN** 请求上下文中已包含插件签发的 placeholder
- **WHEN** 用户停用凭据保护后发送该请求
- **THEN** 仍按 Provider 协议注入 placeholder 完整性指令
- **AND** 判定依据是上下文中是否存在该注入提示词，而非插件是否启用

#### Scenario: 停用判定依据为注入提示词

- **WHEN** 判断某次请求是否需要 placeholder 兼容处理
- **THEN** 依据上下文中是否存在插件注入的完整性提示词判定
- **AND** 不依据插件当前启用状态判定

#### Scenario: 重新启用后恢复主动保护

- **WHEN** 用户重新启用凭据保护
- **THEN** 重新开始识别与 token 化新明文

### Requirement: 停用利害关系在设置页声明

系统 MUST 在凭据保护设置页显式声明停用的行为边界。

#### Scenario: 设置页声明停用语义

- **WHEN** 用户打开凭据保护设置页
- **THEN** 页面展示当前主动保护状态
- **AND** 声明停用后停止的行为
- **AND** 声明停用后仍然保留的能力
- **AND** 声明停用的代价

### Requirement: 插件可自行接管停用语义

系统 SHALL 提供插件接管停用行为的契约，实现该契约的插件在停用时由插件决定各 Extension 行为。

#### Scenario: 语义停用不关闭 Extension

- **GIVEN** 插件实现启用状态感知契约
- **WHEN** 宿主停用该插件
- **THEN** 宿主只回调插件自身状态
- **AND** 不关闭该插件的 Pipeline Entry

#### Scenario: 普通插件保持原有停用行为

- **GIVEN** 插件未实现启用状态感知契约
- **WHEN** 宿主停用该插件
- **THEN** 其全部 Entry 同步停用

## MODIFIED Requirements

### Requirement: 凭据保护规则支持完整增删改查

凭据保护规则存储 SHALL 提供新增、查询、修改、删除，并暴露规则变更通知。

#### Scenario: 修改自定义规则

- **WHEN** 用户在设置页修改某条自定义规则的值
- **THEN** 规则值被更新并持久化到插件自有数据目录
- **AND** 原值不再出现在规则列表中

#### Scenario: 规则变更触发快照刷新

- **WHEN** 规则存储发生任何变更
- **THEN** 插件发出变更通知
- **AND** 宿主重新拉取声明式 UI 快照

### Requirement: 凭据保护内置形态规则只可开关

凭据保护 SHALL 提供内置形态规则基线，只允许开关，不允许删除。

#### Scenario: 关闭内置形态规则

- **WHEN** 用户关闭某项内置形态规则
- **THEN** 该规则不再参与检测与脱敏
- **AND** 状态被持久化

#### Scenario: 关闭内置规则不影响规则集合

- **WHEN** 用户关闭某项内置形态规则
- **THEN** 该规则仍存在于基线中并可重新开启
- **AND** 不从规则集合中移除

### Requirement: 脱敏结果永不含嵌套引用

脱敏 SHALL 在单遍扫描内完成，且 MUST NOT 对自身刚生成的引用或入参中已存在的引用再次脱敏。

#### Scenario: 一段明文只签发一个引用

- **WHEN** 一段文本命中某条形态规则
- **THEN** 输出中该位置恰好替换为一个引用
- **AND** 输出不含嵌套引用（引用内部再套一层引用）

#### Scenario: 已签发引用逐字符透传

- **GIVEN** 入参中包含此前签发的合法引用
- **WHEN** 对该入参执行脱敏
- **THEN** 该引用不被再次替换
- **AND** 其外层不产生新的引用

#### Scenario: 规则竞争顺序不产生嵌套

- **WHEN** 某条规则的随机引用后缀恰好被另一条形态规则识别
- **THEN** 后一条规则不命中该引用
- **AND** 形态规则按先窄后宽竞争同一段区间，先占者胜

#### Scenario: 敏感字段值为引用时不包层

- **GIVEN** 敏感名称字段的值恰好是一个合法引用
- **WHEN** 执行脱敏
- **THEN** 该值原样透传，不被包装为新的引用