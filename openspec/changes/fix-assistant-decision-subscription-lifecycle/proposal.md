## Why

AskUser 已经具备结构化收集单选、多选、数字和文本的底层能力，但当前规格与系统提示把它过度关联到高影响配置决策、资料兜底、Skill 和 Browser Bridge，容易让模型拒绝用户明确要求的 AskUser 测试，也让功能显得依赖 Chrome。现有大型表单 Dialog 同时展示全部字段，缺少逐题引导、跳过、步骤导航和紧凑反馈，不符合用户指定的 Approval Card 体验。

## What Changes

- 将 `assistant.ask_user` 明确为通用 Human-in-the-loop 工具：用户明确要求测试、收集偏好、澄清歧义、确认行动或输入结构化数据时均可直接调用。
- 明确 AskUser 不依赖任何 Skill、Browser Bridge、Chrome Extension、搜索或资料通道；只有真实网页任务才按 Skill 指引启用 Bridge。
- 保留决策订阅的请求级生命周期：导航不激活，真实 Assistant 请求开始后订阅，请求结束后解除。
- 将 AskUser Dialog 重做为主题协调的紧凑 Approval Card：一页一个字段、步骤导航、跳过、继续/提交、关闭和键盘操作。
- 继续支持现有四种字段类型和 Broker Submit/Cancel 契约，不把网页参考中的 React 实现或固定配色直接移植到 Avalonia。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `assistant-user-decisions`: 扩展 AskUser 的通用可用性，并定义 Approval Card 的分页、导航、跳过、取消、验证与提交行为。

## Impact

- 影响 Assistant 系统提示、`assistant.ask_user` 工具描述和相关契约测试。
- 影响 `AskUserDialogViewModel`、字段 ViewModel、`AskUserDialog.axaml` 与代码后置。
- 增加本地化资源、分页与交互测试；不改变数据库、Browser Bridge、SkillStore 或 UserDecisionBroker 的外部协议。
