## MODIFIED Requirements

### Requirement: Assistant 必须支持结构化用户决策请求
系统 SHALL 支持由助手直接发起包含标题、问题、字段、选项、默认值、必填标记和可取消状态的结构化 AskUser 请求，并 SHALL 支持单选、多选、数字输入和自由文本字段。AskUser SHALL 是通用 Assistant 工具，不得要求预先加载 Skill、启动 Browser Bridge、连接 Chrome Extension或具备搜索能力。

#### Scenario: 用户明确测试 AskUser
- **WHEN** 用户要求展示或验收 AskUser 的单选、多选、数字或文本交互
- **THEN** 助手直接调用 `assistant.ask_user`，不得以缺少 Skill、Bridge、Chrome 或资料通道为由拒绝

#### Scenario: 发起组合决策
- **WHEN** 助手需要同时确认上下文窗口、reasoning levels 和补充说明
- **THEN** 系统可以在一个 AskUser 请求中按字段逐页展示对应的单选、多选和文本输入

#### Scenario: 用户取消决策
- **WHEN** 用户关闭或取消允许取消的 AskUser Card
- **THEN** 助手收到结构化取消结果，不得把取消解释为用户同意任何默认值

### Requirement: AskUser 必须控制询问时机和信息边界
系统 SHALL 允许调用方说明询问原因和影响摘要；普通内部步骤默认不强制询问，但用户明确请求、输入缺失、存在歧义、需要偏好或行动确认时 SHALL 允许直接使用 AskUser。桌面端 SHALL 仅在活动 Assistant 请求期间订阅用户决策 Broker，页面挂载、导航、模型选择或配置浏览不得单独激活订阅；AskUser 展示内容不得包含 API Key、Authorization、完整请求正文或其他敏感数据。

#### Scenario: 高影响配置决策
- **WHEN** Catalog Profile 缺少关键字段或配置变化需要用户决定是否稍后重启 Codex
- **THEN** 助手可以发起 AskUser，并展示安全摘要和可选行动

#### Scenario: 普通内部步骤
- **WHEN** 助手拥有完成普通内部步骤所需的全部信息且用户没有要求确认
- **THEN** 系统不强制额外弹出 AskUser，但不得禁止模型在合理场景主动调用

#### Scenario: 通用澄清与偏好收集
- **WHEN** 用户输入存在歧义、缺少必要选择，或用户要求收集偏好
- **THEN** 助手可以直接调用 AskUser，不需要加载任何领域 Skill

#### Scenario: AskUser 与 Browser Bridge 解耦
- **WHEN** AskUser 请求不涉及网页读取或浏览器自动化
- **THEN** 系统不得启动 Browser Bridge，也不得要求 Chrome Extension 在线

#### Scenario: 页面导航不激活决策订阅
- **WHEN** 用户进入或离开 Assistant、Provider、控制台页面，且没有正在发送的 Assistant 请求
- **THEN** 系统不订阅用户决策 Broker，也不记录“助手决策订阅已激活”

#### Scenario: 用户请求激活决策订阅
- **WHEN** 用户发送 Assistant 请求且服务初始化成功
- **THEN** 系统在 AgentLoop 和工具执行前订阅用户决策 Broker，使模型可以直接或在加载 Skill 后调用 `assistant.ask_user`

#### Scenario: 用户请求结束解除决策订阅
- **WHEN** Assistant 请求完成、失败、取消或因页面离开而停止处理用户决策
- **THEN** 系统解除 Broker 订阅，并对已领取的决策请求执行幂等取消，不能遗留等待项

## ADDED Requirements

### Requirement: AskUser 必须以逐题 Approval Card 完成输入和提交
桌面端 SHALL 使用与应用主题协调的紧凑 Approval Card，一次展示一个字段，并提供步骤计数、前后导航、跳过、继续/提交和允许取消时的关闭入口。字段切换 SHALL 保留输入，最终 SHALL 继续通过 Broker 提交结构化结果。

#### Scenario: 逐题浏览组合请求
- **WHEN** AskUser 请求包含多个字段
- **THEN** Card 每次只展示当前字段，显示当前位置与总数，并允许用户前后查看且不丢失已输入值

#### Scenario: 跳过可选字段
- **WHEN** 当前字段不是必填且用户选择跳过
- **THEN** 系统清空当前字段并进入下一字段；若已是最后字段，则验证并提交整个请求

#### Scenario: 必填字段不能跳过
- **WHEN** 当前字段是必填且没有有效值
- **THEN** 跳过与继续操作不可完成，并在用户尝试前进或提交时显示当前字段的安全校验摘要

#### Scenario: 单选自动前进
- **WHEN** 用户在非末页选择一个单选项
- **THEN** Card 在短暂选择反馈后自动进入下一字段，所选 option id 保持不变

#### Scenario: 多选数字文本等待确认
- **WHEN** 当前字段是多选、数字或文本
- **THEN** Card 保留当前输入并等待用户点击继续或提交

#### Scenario: 取消整个请求
- **WHEN** 请求允许取消且用户点击关闭按钮或按 Escape
- **THEN** Card 取消整个请求并关闭，不提交部分字段或默认值

#### Scenario: 请求不允许取消
- **WHEN** 请求的 `allow_cancel` 为 false
- **THEN** Card 不显示关闭入口，Escape 不取消请求，用户必须完成有效输入或由外部请求生命周期终止
