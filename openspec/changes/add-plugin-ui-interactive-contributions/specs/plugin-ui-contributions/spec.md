# plugin-ui-contributions Specification（变更增量）

## ADDED Requirements

### Requirement: 插件声明式 UI 支持交互节点与命令回传

插件 SHALL 能在协议版本 2 的 Contribution 中声明交互节点（按钮、文本字段、开关字段、下拉字段）；
宿主 SHALL 把用户操作回传为命令，由插件自行校验与落库。交互能力 MUST 由 Manifest 逐 Contribution 声明，
未声明为交互的 Contribution MUST NOT 接收命令。

#### Scenario: 插件提供可交互设置页

- **WHEN** 插件 Manifest 把某个 Contribution 声明为 `interactive`
- **THEN** Router 在该挂载位置渲染交互控件并挂载命令通道
- **THEN** 用户点击按钮或改变开关/下拉值时，Router 把命令标识、节点参数与当前表单字段值回传给插件

#### Scenario: 命令执行后刷新快照

- **WHEN** 插件处理完命令并发出 UI 失效通知
- **THEN** Router 重新获取该插件的 Contribution 快照并重建对应区域
- **THEN** 其他插件的卡片不重新构建

#### Scenario: 命令结果反馈

- **WHEN** 插件返回命令结果且包含安全摘要消息
- **THEN** Router 按结果语气把该消息 Toast 给用户
- **THEN** Router 不把命令参数或字段值写入日志

#### Scenario: 未声明交互的 Contribution

- **WHEN** 插件 Contribution 未声明 `interactive` 却收到命令
- **THEN** Router 拒绝该命令并返回未处理
- **THEN** 该插件的 UI 快照与 Pipeline 能力不受影响

### Requirement: 宿主隔离无效与非法的交互回传

Router MUST 校验 Contribution 的协议版本与交互节点结构（节点标识、命令标识、标签、参数数量与长度）。
Router MUST 校验命令回传的插件标识、命令标识、参数数量与参数长度。
任何非法输入或插件异常 MUST 只记录不含敏感内容的安全诊断，不得影响同一插件的 Pipeline 能力或其他插件。

#### Scenario: v1 快照包含交互节点

- **WHEN** 插件以协议版本 1 返回包含交互节点的 Contribution
- **THEN** Router 隔离该 Contribution 并记录 `INTERACTIVE_NODE_REQUIRES_V2`
- **THEN** 该插件的 Extension 仍可继续运行

#### Scenario: 命令参数超限

- **WHEN** 命令参数数量超过宿主上限或单个参数长度超限
- **THEN** Router 拒绝该命令并返回未处理
- **THEN** 诊断不包含参数内容

#### Scenario: 命令处理器异常

- **WHEN** 插件命令处理器抛出异常
- **THEN** Router 记录 `COMMAND_FAILURE` 诊断并返回未处理
- **THEN** 该插件的 Pipeline 仍可继续执行

### Requirement: maskit 插件贡献规则设置页

第一方 maskit 插件 SHALL 在插件卡片正文贡献只读概览，在插件详情正文贡献可交互的规则设置页，
支持自定义规则的增、删、改、查与启停，内置形态规则的开关，以及密钥前缀的增删。
设置页 MUST 由插件运行时动态贡献，宿主 MUST NOT 内置任何 maskit 专属页面。

#### Scenario: 新增并删除自定义规则

- **WHEN** 用户在设置页填写类别与规则内容并点击添加
- **THEN** 插件校验后落库并返回安全摘要，快照中出现该规则
- **WHEN** 用户点击某条规则的删除
- **THEN** 该规则从插件自有存储中移除且快照同步更新

#### Scenario: 重复或非法规则被拒绝

- **WHEN** 用户提交已存在的同类规则、空类别、空内容或无法解析的正则
- **THEN** 插件拒绝落库并返回失败语气的安全摘要
- **THEN** 已有规则不受影响

#### Scenario: 规则值不进入日志与 Toast

- **WHEN** 任意规则命令被执行
- **THEN** 日志与 Toast 只出现安全摘要，不出现规则值或命中原文
