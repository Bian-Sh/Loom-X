# assistant-user-decisions Specification

## Purpose
为 AI 助手提供统一的用户决策交互，使 Profile 构建、重启提醒和资料歧义处理可以一次性收集单选、多选、数值与文本输入，而不是依赖自然语言猜测。

## Requirements

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

### Requirement: 资料收集必须优先复用已有能力且不绕过网站安全机制
系统 SHALL 优先使用当前 Assistant 模型已经提供的搜索或资料能力；不可用时 SHALL 允许通过现有 Chrome Extension/Browser Bridge 读取用户明确打开并授权的页面；系统 MUST NOT 实现第三方搜索 API Key、验证码绕过、登录墙绕过、Cloudflare/JS challenge 绕过、Cookie 注入、TLS fingerprint 或浏览器指纹伪装。

#### Scenario: 模型具备搜索能力
- **WHEN** 当前 Assistant 模型提供可用的官方搜索能力
- **THEN** 助手优先使用该能力获取资料，不要求用户配置新的搜索 API Key

#### Scenario: 模型没有搜索能力但浏览器已连接
- **WHEN** Assistant 没有搜索能力且 Chrome Extension 已连接
- **THEN** 助手可以打开官方文档或 Provider 页面，并读取用户授权的页面内容

#### Scenario: 页面需要用户处理障碍
- **WHEN** 页面出现登录、验证码、Cloudflare 或 JS challenge
- **THEN** 助手暂停并提示用户自行处理，处理完成后再继续读取，不尝试绕过页面安全机制

#### Scenario: 没有任何资料通道
- **WHEN** Assistant 没有搜索能力、Browser Bridge 未连接且用户未提供资料
- **THEN** 助手明确说明无法验证资料，并通过 AskUser 请求用户提供结论或文档内容

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

### Requirement: AskUser 选择字段必须支持可选自由输入
系统 SHALL 允许 `single_select` 和 `multi_select` 字段通过 `allow_custom_input` 启用自由输入，并 SHALL 允许调用方通过 `custom_input_placeholder` 自定义占位提示。未提供占位提示时，桌面端 SHALL 使用“我有其他想法...”作为默认提示。自由输入与预设选项 SHALL 可以共存并同时提交，且未启用该能力的选择字段 SHALL 保持现有交互和结果结构。

#### Scenario: 选择字段展示自由输入框
- **WHEN** 单选或多选字段设置 `allow_custom_input=true`
- **THEN** 桌面端在选项列表下方展示自由输入框，并使用调用方指定的占位提示或默认的“我有其他想法...”

#### Scenario: 用户要求选择题与输入框同页时使用单字段建模
- **WHEN** 用户要求在单选或多选选项下方、同一个弹窗内提供输入框，或指定该输入框的字数限制（例如80字）
- **THEN** assistant.ask_user 调用 SHALL 只创建一个选择字段，设置 `allow_custom_input=true`，并将字数限制写入该字段的 `max_length`；不得新增独立 `text` 字段，因为每个字段会独立分页

#### Scenario: 未启用时保持原有界面
- **WHEN** 选择字段未设置 `allow_custom_input` 或其值为 false
- **THEN** 桌面端不展示自由输入框，字段继续只接受预设选项

#### Scenario: 输入自由内容保留已有选择
- **WHEN** 用户在选择字段的自由输入框中输入或编辑内容
- **THEN** 系统保留该字段已有的单选或多选 option id，并同时保留用户输入原文

#### Scenario: 调整选项保留自由输入
- **WHEN** 用户已输入自由内容后选择、切换或取消任一预设选项
- **THEN** 系统保留该字段的自由输入，并按既有单选或多选规则更新 option id

#### Scenario: 自由输入满足必填选择题
- **WHEN** 必填选择字段没有有效预设选项但包含非空自由输入
- **THEN** 字段校验通过；多选字段的 `min_selections` 约束不阻止该自由输入作为替代答案提交

#### Scenario: 空白自由输入不满足校验
- **WHEN** 必填选择字段仅包含空白自由输入且没有有效预设选项
- **THEN** 系统按缺少有效值处理并显示安全校验摘要

### Requirement: AskUser 必须把用户输入原文返回给助手
系统 SHALL 在 AskUser 成功提交时把文本字段的实际字符串写入 `values`，不得仅返回输入存在性标记。选择字段使用自由输入时，系统 SHALL 在结果根对象的 `custom_inputs` 映射中按字段 id 返回用户原文；若用户同时选择预设选项，`values` SHALL 保留该选择字段的 option id 或 option id 数组，若没有预设选择则保留现有类型对应的空值。系统 MUST NOT 因返回原文而把用户输入记录到运行日志。

#### Scenario: 文本字段返回实际内容
- **WHEN** 用户在文本字段输入内容并提交 AskUser
- **THEN** 工具结果的 `values` 对应字段包含实际字符串，而不是 `{ "provided": true }`

#### Scenario: 单选自由输入返回实际内容
- **WHEN** 用户使用单选字段的自由输入提交答案
- **THEN** `values` 中该字段为 null，`custom_inputs` 中同名字段包含用户输入原文

#### Scenario: 多选自由输入返回实际内容
- **WHEN** 用户使用多选字段的自由输入提交答案
- **THEN** `values` 中该字段为空数组，`custom_inputs` 中同名字段包含用户输入原文

#### Scenario: 预设选择保持兼容
- **WHEN** 用户使用预设 option 完成选择字段且没有自由输入
- **THEN** `values` 继续返回现有的 option id 或 option id 数组，`custom_inputs` 不包含该字段

#### Scenario: 用户输入不进入日志
- **WHEN** AskUser 解析、校验、提交或序列化包含文本或选择题自由输入的结果
- **THEN** 运行日志只记录安全摘要，不记录用户输入原文

### Requirement: AskUser 取消必须在当前助手轮次内保持终态
系统 SHALL 在用户取消 AskUser 后向助手返回 `cancelled=true`、空 `values` 和空 `custom_inputs`。当前 AgentLoop SHALL 继续允许模型生成取消摘要，但在该轮剩余步骤中不得再次展示 AskUser；即使模型仍发出重复的 `assistant.ask_user` 调用，也 SHALL 复用原取消结果而不重新进入 Broker 或 UI。

#### Scenario: 点击卡片关闭按钮后不再弹出
- **WHEN** 请求允许取消且用户点击 AskUser 卡片右上角关闭按钮
- **THEN** 当前请求完成为 `cancelled=true`，卡片关闭，并且当前助手轮次内后续模型步骤不再获得或执行新的 AskUser 面板调用

#### Scenario: 模型在取消后重复调用 AskUser
- **WHEN** 模型已经收到 `cancelled=true` 后仍生成新的 `assistant.ask_user` 工具调用
- **THEN** AgentLoop 不再次调用 AskUser Handler、不发布新的 Pending 请求，而是向模型复用原结构化取消结果

#### Scenario: 取消后助手可以准确总结
- **WHEN** AskUser 取消结果已进入会话工具消息
- **THEN** AgentLoop 继续一次正常模型处理流程，使助手可以基于 `cancelled=true` 输出取消摘要，而不是把后续提交误写为 `cancelled=false`
