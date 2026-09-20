## MODIFIED Requirements

### Requirement: Assistant 必须支持结构化用户决策请求
系统 SHALL 支持由助手发起包含标题、问题、字段、选项、默认值、必填标记和可取消状态的结构化 AskUser 请求，并 SHALL 支持单选、多选、数字输入和自由文本字段。AskUser SHALL 通过 Assistant 输入框上方的应用内悬浮卡片展示，不得依赖独立模态 Window。

#### Scenario: 发起组合决策
- **WHEN** 助手需要同时确认上下文窗口、reasoning levels 和补充说明
- **THEN** 系统可以在一个 AskUser 请求中逐题展示对应的单选、多选和文本输入字段

#### Scenario: 用户取消决策
- **WHEN** 请求允许取消且用户点击悬浮卡片底部的取消操作
- **THEN** 助手收到结构化取消结果，不得把取消解释为用户同意任何默认值

#### Scenario: 卡片不创建第二顶层窗口
- **WHEN** 桌面端展示 AskUser
- **THEN** 卡片位于当前 Assistant 顶层窗口内部，不创建独立 Window、标题栏或右上角关闭按钮

### Requirement: AskUser 必须暂停并恢复当前助手执行
系统 SHALL 在等待用户输入期间暂停当前工具调用或 Agent 步骤，保留请求标识和会话上下文，并在用户提交后恢复原流程。等待期间用户发送的其他消息 SHALL 进入当前会话待发送队列，不得并发注入正在等待的 AgentLoop。

#### Scenario: 用户提交选择
- **WHEN** 用户完成所有必填字段并提交
- **THEN** 系统按字段 id 返回结构化结果，助手可以继续执行原操作

#### Scenario: Assistant 运行中关闭页面
- **WHEN** 悬浮卡片所属页面被关闭或会话被取消
- **THEN** 等待中的请求收到取消/失败结果，不能永久阻塞 Agent Loop

#### Scenario: AskUser 提交不提前发送队列消息
- **WHEN** 用户提交 AskUser 后当前 AgentLoop 继续执行工具或生成最终回答
- **THEN** 系统等待当前 Assistant 轮次完整结束后才允许队首消息出队

### Requirement: AskUser 必须控制询问时机和信息边界
系统 SHALL 允许调用方说明询问原因和影响摘要。普通步骤在信息充分且用户未要求确认时不强制询问，但系统不得把该默认行为解释成 AskUser 的能力限制。当用户要求测试、收集偏好、补充必要输入、澄清歧义或确认行动时，系统 SHALL 允许直接使用 AskUser。AskUser 展示内容不得包含 API Key、Authorization、完整请求正文或其他敏感数据。

#### Scenario: 高影响配置决策
- **WHEN** Catalog Profile 缺少关键字段或配置变化需要用户决定是否稍后重启 Codex
- **THEN** 助手可以发起 AskUser，并展示安全摘要和可选行动

#### Scenario: 普通内部步骤
- **WHEN** 助手拥有完成普通内部步骤所需的全部信息且用户没有要求确认
- **THEN** 系统不强制额外展示 AskUser，但不得禁止模型在合理场景主动调用

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

### Requirement: AskUser 必须显示为输入框上方的悬浮 Approval Card
桌面端 SHALL 在 Assistant 输入框正上方使用与应用主题协调的悬浮卡片，一次展示一个字段。该卡片 SHALL 位于当前 Assistant 顶层窗口内部，不得进入消息历史，也不得占用固定消息流条目。

#### Scenario: 卡片锚定输入框
- **WHEN** Assistant 领取 AskUser 请求
- **THEN** 卡片悬浮在输入框上方并随输入框位置保持稳定间距，消息区仍可见且可滚动

#### Scenario: 逐题浏览组合请求
- **WHEN** AskUser 请求包含多个字段
- **THEN** 卡片每次只展示当前字段，显示当前位置与总数，并允许用户前后查看且不丢失已输入值

#### Scenario: 跳过可选字段
- **WHEN** 当前字段不是必填且用户选择跳过
- **THEN** 系统清空当前字段并进入下一字段；若已是最后字段，则验证并提交整个请求

#### Scenario: 必填字段不能跳过
- **WHEN** 当前字段是必填且没有有效值
- **THEN** 跳过与继续操作不可完成，并显示当前字段的安全校验摘要

#### Scenario: 单选自动前进
- **WHEN** 用户在非末页选择一个单选项
- **THEN** 卡片在短暂选择反馈后自动进入下一字段，所选 option id 保持不变

#### Scenario: 多选数字文本等待确认
- **WHEN** 当前字段是多选、数字或文本
- **THEN** 卡片保留当前输入并等待用户点击继续或提交

#### Scenario: 请求不允许取消
- **WHEN** 请求的 `allow_cancel` 为 false
- **THEN** 卡片不显示取消整个请求的操作，用户必须完成有效输入或由外部请求生命周期终止

#### Scenario: 卡片生命周期被外部请求终止
- **WHEN** 页面离开、会话切换、用户停止生成、请求失败或 ViewModel 被释放
- **THEN** 系统幂等取消已领取请求、移除悬浮卡片，并忽略迟到的提交或取消事件

### Requirement: 活动 Assistant 轮次期间发送的后续消息必须进入可删除队列
桌面端 SHALL 在 Assistant 请求运行期间保持输入框可编辑。用户发送的新消息 SHALL 先进入当前会话的待发送队列，而不是并发注入当前 AgentLoop。队列 SHALL 显示发送顺序，并允许删除尚未出队的项目。

#### Scenario: AskUser 等待期间发送后续消息
- **WHEN** AskUser 卡片正在等待回答且用户从输入框发送一条新消息
- **THEN** 系统清空输入框并把消息显示为待发送队列项，当前 AskUser 内容和 AgentLoop 不发生改变

#### Scenario: 删除未发送消息
- **WHEN** 用户删除仍在队列中的消息
- **THEN** 该消息从队列移除且不进入会话历史，也不会发送给模型

#### Scenario: 当前轮次完整结束后出队
- **WHEN** 活动 Assistant 轮次在 AskUser 返回后继续执行并最终正常结束
- **THEN** 系统才把队首转为正式用户消息并启动下一轮，不能在 AskUser 卡片刚提交时提前发送

#### Scenario: 多条消息顺序处理
- **WHEN** 当前会话存在多条待发送消息
- **THEN** 系统每次只执行一轮，并在上一轮正常结束后按队列顺序发送下一条

#### Scenario: 失败或停止时暂停队列
- **WHEN** 当前轮次失败、服务不可用或被用户停止
- **THEN** 系统停止自动出队并保留剩余队列项目，等待用户删除或后续重新触发

#### Scenario: 队列不能跨会话误发
- **WHEN** 用户在队列尚未清空时切换到其他会话
- **THEN** 原队列仍绑定创建它的 SessionId，不得作为新会话消息发送

#### Scenario: 简版能力不限制后续 Codex 风格扩展
- **WHEN** 当前版本只提供顺序显示和删除
- **THEN** 队列数据模型仍使用稳定标识和显式状态，不得把不可编辑、不可排序或不可 Steer 固化为外部协议
