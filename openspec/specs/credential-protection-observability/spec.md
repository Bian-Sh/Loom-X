# credential-protection-observability Specification

## Purpose
定义 Credential Protection 如何在不记录或泄露敏感内容的前提下统计当前进程内的脱敏、恢复和异常结果，并通过插件自有声明式 UI 展示安全摘要。

## Requirements

### Requirement: Credential Protection 统计请求脱敏结果
Credential Protection SHALL 对当前进程内进入请求 Extension 的请求总数、实际发生敏感内容替换的请求数以及实际替换的敏感词项数进行线程安全累计。只注入 placeholder 完整性指令而未替换敏感内容的请求 MUST NOT 计入已脱敏请求数。

#### Scenario: 请求包含多个敏感词项
- **WHEN** 一个请求实际替换三个敏感词项
- **THEN** 总请求数增加一
- **THEN** 已脱敏请求数增加一
- **THEN** 累计脱敏词项数增加三

#### Scenario: 普通请求原样通过
- **WHEN** 一个请求不包含需要替换的敏感内容
- **THEN** 总请求数增加一
- **THEN** 已脱敏请求数和累计脱敏词项数保持不变

#### Scenario: 请求只包含既有 placeholder
- **WHEN** 一个请求只触发 placeholder 完整性指令而没有替换新的敏感内容
- **THEN** 总请求数增加一
- **THEN** 该请求不计入已脱敏请求数

### Requirement: Credential Protection 统计恢复与异常
Credential Protection SHALL 按响应统计成功恢复至少一个本地签发 placeholder 的已还原回复数，并累计请求或响应处理过程中被捕获且转换为 fail-closed 结果的异常数。

#### Scenario: 一个响应恢复多个 placeholder
- **WHEN** 一个 Provider 响应成功恢复多个本地 placeholder
- **THEN** 已还原回复数只增加一

#### Scenario: 响应没有可恢复 placeholder
- **WHEN** 一个 Provider 响应未恢复任何本地 placeholder
- **THEN** 已还原回复数保持不变

#### Scenario: 脱敏或恢复抛出异常
- **WHEN** 请求脱敏或响应恢复过程中发生内部异常
- **THEN** 异常数增加一
- **THEN** 现有 fail-closed 行为继续阻止未安全处理的数据流动

### Requirement: Credential Protection 自行声明观测 UI
Credential Protection SHALL 通过通用插件 UI Contribution 契约声明卡片正文，不得要求 Router 根据插件 ID、指标名称或凭据语义写死界面。观测区域 SHALL 展示已脱敏请求数与总请求数的组合值、累计脱敏词项、已还原回复数和异常数。

#### Scenario: 展示请求比例和词项数
- **WHEN** 当前进程已处理一百五十个请求，其中十五个发生脱敏且累计替换二十个词项
- **THEN** 插件声明的已脱敏请求主值显示为 `15/150`
- **THEN** 辅助文字显示累计脱敏词项二十

#### Scenario: 展示恢复和异常
- **WHEN** 当前进程已还原五个响应并发生两个处理异常
- **THEN** 插件声明的已还原回复主值显示五
- **THEN** 插件声明的异常主值显示二并使用警示语义色

### Requirement: 观测数据不得包含敏感内容
Credential Protection 的观测状态、UI Contribution、失效通知和相关日志 MUST 只包含计数与安全标识，不得保存或公开敏感原文、请求正文、响应正文、用户 prompt、凭据 placeholder、Authorization 或自定义 Header 值。观测计数 SHALL 仅存在于当前进程内并在应用重启后清零。

#### Scenario: 获取观测 Contribution
- **WHEN** Router 获取 Credential Protection 的观测 Contribution
- **THEN** 返回内容只包含布局、插件自有本地化文案、图标和数字摘要
- **THEN** 返回内容不包含任何命中的敏感值或 placeholder

#### Scenario: 应用重新启动
- **WHEN** LoomX 进程结束后重新启动
- **THEN** Credential Protection 的观测计数从零开始
- **THEN** 长期 Credential Vault 映射不受观测计数清零影响
