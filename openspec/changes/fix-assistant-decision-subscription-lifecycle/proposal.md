## Why

AskUser 已具备结构化收集单选、多选、数字和文本的底层能力，但旧规格和系统提示把它过度关联到高影响配置决策、资料兜底、Skill 与 Browser Bridge，导致模型可能拒绝用户明确要求的 AskUser 测试。此前实现又把 Approval Card 放进独立模态 Window；真实验收表明这虽然能工作，但不符合“锚定在 AI 输入框上方的悬浮卡片”预期，并且窗口级输入与截图工具容易产生歧义。

同时，AskUser 等待期间完全锁死输入区会让用户感到受限。更合适的交互是保留输入能力，把运行期间发送的后续消息放入当前会话队列，在活动 Assistant 轮次完整结束后顺序发送，并允许删除尚未发送的项目。

## What Changes

- 将 `assistant.ask_user` 明确为通用 Human-in-the-loop 工具：用户要求测试、收集偏好、澄清歧义、确认行动或输入结构化数据时均可直接调用。
- 明确 AskUser 不依赖任何 Skill、Browser Bridge、Chrome Extension、搜索或资料通道；只有真实网页任务才按 Skill 指引启用 Bridge。
- 保留决策订阅的请求级生命周期：导航不激活，真实 Assistant 请求开始后订阅，请求结束后解除。
- 将 AskUser 从独立模态 Dialog 改为 Assistant 输入框上方的应用内悬浮卡片：不进入消息历史、不占固定消息流布局、不创建第二个顶层窗口。
- 去除标题栏与右上角关闭按钮；允许取消时在卡片底部提供低强调取消操作。
- 为 Assistant 输入区增加简版消息队列：运行期间发送即入队、显示顺序与删除入口，当前轮次完整结束后依次出队。
- 完整 Codex 风格队列的编辑、排序、立即发送、Steer、持久化等能力作为独立后续需求记录，不把简版限制固化成长期协议。
- 修复 Avalonia 生命周期测试在异步等待后离开 UI 线程造成的顺序依赖，使验证命令可重复。

## Capabilities

### New Capabilities

无。本次简版队列仍属于 `assistant-user-decisions` 与 Assistant 交互生命周期的扩展；完整队列后续单独建立 OpenSpec change。

### Modified Capabilities

- `assistant-user-decisions`：扩展 AskUser 的通用可用性，定义输入框上方悬浮卡片，以及运行中消息排队、删除和顺序出队行为。

## Impact

- 影响 Assistant 系统提示、`assistant.ask_user` 工具描述和相关契约测试。
- 影响 `AssistantViewModel`、AskUser 状态 ViewModel、`AssistantView.axaml`、AskUser 视图与代码后置。
- 移除运行时对 AskUser 独立 Window 的依赖，但不改变数据库、Browser Bridge、SkillStore 或 UserDecisionBroker 的外部协议。
- 增加会话内临时队列状态；简版队列不跨应用重启持久化。
- 增加本地化、悬浮布局、队列和 Avalonia UI 线程稳定性测试。
