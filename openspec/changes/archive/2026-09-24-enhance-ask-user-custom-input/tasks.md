## 1. 请求契约与领域模型

- [x] 1.1 先为 `allow_custom_input`、`custom_input_placeholder`、选择字段 `max_length` 适用范围和非法组合补充失败测试，并运行 `UserDecisionModelsTests` 与 `AssistantToolsTests` 确认因能力缺失而失败 <!-- comet-task:askuser-1-1 -->
- [x] 1.2 扩展 `UserDecisionField`、AskUser JSON Schema、参数解析和请求校验，并运行上述定向测试确认通过 <!-- comet-task:askuser-1-2 -->

## 2. 提交校验与工具结果

- [x] 2.1 先为必填选择题自由输入、空白/超长输入、未授权自由输入和 `UserDecisionResult.CustomInputs` 补充失败测试，并运行定向测试确认预期失败 <!-- comet-task:askuser-2-1 -->
- [x] 2.2 扩展提交校验、Broker 提交接口和结果快照，使 `Values` 与 `CustomInputs` 独立复制与校验，并运行 Broker/模型测试确认通过 <!-- comet-task:askuser-2-2 -->
- [x] 2.3 先更新工具结果测试要求 text 返回实际字符串且根对象包含 `custom_inputs`，确认失败后修改序列化实现，并运行 `AssistantToolsTests`、`AssistantServiceTests` 确认通过 <!-- comet-task:askuser-2-3 -->

## 3. 卡片交互与本地化

- [x] 3.1 先为单选/多选自由输入的状态保留、选项共存、跳过清空、必填替代校验及单选不自动前进补充失败测试，并运行 AskUser ViewModel/视图契约测试确认失败 <!-- comet-task:askuser-3-1 -->
- [x] 3.2 扩展选择字段 ViewModel 和 `AskUserCard` 模板，在选项下方展示无额外标签的输入框，使用本地化默认提示“我有其他想法...”，并运行定向测试确认通过 <!-- comet-task:askuser-3-2 -->
- [x] 3.3 补齐所有 Locale 资源和源码契约检查，运行 `AskUserDialogContractTests`、`AssistantViewStyleTests` 确认输入框显示条件、watermark 与现有卡片布局兼容 <!-- comet-task:askuser-3-3 -->

## 4. 集成验证与交付

- [x] 4.1 运行 AskUser、UserDecision、AssistantTools、AssistantService 和 AssistantViewModel 相关测试，确认选择结果兼容、实际文本回传和日志安全边界 <!-- comet-task:askuser-4-1 -->
- [x] 4.2 运行 `openspec validate enhance-ask-user-custom-input --strict`、完整测试和 Release 构建，确认无失败、无编译错误 <!-- comet-task:askuser-4-2 -->
- [x] 4.3 发布桌面端到 `outputs/2026-09-20-<time>-ask-user-custom-input`，使用 `cua-driver` 验证单选、多选自由输入、选择与文字共存、默认提示和提交后的 AI 可见结果 <!-- comet-task:askuser-4-3 -->
## 5. 模型调用契约与同页建模回归

- [x] 5.1 为工具描述、Schema description 和系统提示补充“字段独立分页、选择题同页输入使用 allow_custom_input、字数写入同一字段 max_length、不得新增 text 字段”的失败测试与实现，并运行定向测试确认通过 <!-- comet-task:askuser-5-1 -->
- [x] 5.2 为右上角取消后卡片重弹补充 AgentLoop 失败测试；取消后本轮移除 AskUser 工具，并对模型重复调用复用 `cancelled=true`，确认不再进入 Handler/Broker/UI 且助手仍可生成取消摘要 <!-- comet-task:askuser-5-2 -->
