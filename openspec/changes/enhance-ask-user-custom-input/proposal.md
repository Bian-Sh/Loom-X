## Why

AskUser 的选择题只能返回预设 option id，用户遇到所有选项均不符合意图时无法直接补充真实想法；同时自由文本字段当前只向 AI 返回 `provided=true`，实际内容被丢弃，导致 AskUser 无法完成有效澄清。

## What Changes

- 为 `single_select` 与 `multi_select` 字段增加可选的自由输入能力，并允许调用方提供输入框占位提示。
- 在选择题选项下方以“我有其他想法...”作为默认占位提示展示输入框；自由输入与预设选项互斥。
- 必填选择题可以由有效预设选项或非空自由输入任一满足。
- AskUser 结果新增 `custom_inputs` 映射，按字段 id 向 AI 返回选择题的自由输入原文，同时保留 `values` 中既有选择结果结构。
- 自由文本字段直接返回用户实际输入字符串，不再仅返回 `{ "provided": true }`。
- 补充 Schema、解析、校验、ViewModel、Avalonia 视图和工具结果的回归测试。
- 明确 assistant.ask_user 的模型可见建模规则：每个字段独立分页；选择题同页输入必须使用同一字段的 allow_custom_input，不得拆成独立 text 字段。
- 修复 AskUser 取消后的生命周期：保留 cancelled=true 供助手总结，并在当前轮次屏蔽后续重复 AskUser 调用，避免卡片重新弹出。

## Capabilities

### New Capabilities

- 无。

### Modified Capabilities

- `assistant-user-decisions`: 扩展选择题自由输入、必填校验与 AskUser 结果回传契约。

## Impact

- 公开接口：`assistant.ask_user` 参数 Schema 和工具结果 JSON 契约。
- 数据模型与校验：`UserDecisionField`、`UserDecisionResult`、`UserDecisionValidator`。
- 桌面交互：AskUser 字段 ViewModel、悬浮卡片选择题模板及键盘/选择互斥行为。
- 测试：AssistantTools、UserDecision、AskUser ViewModel 与视图契约测试。
- Agent 循环：AskUser 取消后本轮移除工具可见性，并对模型的重复调用复用取消结果。
- 不引入新依赖，不修改数据库 Schema，不记录用户输入到日志。
