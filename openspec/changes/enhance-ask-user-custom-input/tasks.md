## 1. 请求契约与领域模型

- [ ] 1.1 先为 `allow_custom_input`、`custom_input_placeholder`、选择字段 `max_length` 适用范围和非法组合补充失败测试，并运行 `UserDecisionModelsTests` 与 `AssistantToolsTests` 确认因能力缺失而失败
- [ ] 1.2 扩展 `UserDecisionField`、AskUser JSON Schema、参数解析和请求校验，并运行上述定向测试确认通过

## 2. 提交校验与工具结果

- [ ] 2.1 先为必填选择题自由输入、空白/超长输入、未授权自由输入和 `UserDecisionResult.CustomInputs` 补充失败测试，并运行定向测试确认预期失败
- [ ] 2.2 扩展提交校验、Broker 提交接口和结果快照，使 `Values` 与 `CustomInputs` 独立复制与校验，并运行 Broker/模型测试确认通过
- [ ] 2.3 先更新工具结果测试要求 text 返回实际字符串且根对象包含 `custom_inputs`，确认失败后修改序列化实现，并运行 `AssistantToolsTests`、`AssistantServiceTests` 确认通过

## 3. 卡片交互与本地化

- [ ] 3.1 先为单选/多选自由输入的状态保留、选项互斥、跳过清空、必填替代校验及单选不自动前进补充失败测试，并运行 AskUser ViewModel/视图契约测试确认失败
- [ ] 3.2 扩展选择字段 ViewModel 和 `AskUserCard` 模板，在选项下方展示无额外标签的输入框，使用本地化默认提示“我有其他想法...”，并运行定向测试确认通过
- [ ] 3.3 补齐所有 Locale 资源和源码契约检查，运行 `AskUserDialogContractTests`、`AssistantViewStyleTests` 确认输入框显示条件、watermark 与现有卡片布局兼容

## 4. 集成验证与交付

- [ ] 4.1 运行 AskUser、UserDecision、AssistantTools、AssistantService 和 AssistantViewModel 相关测试，确认选择结果兼容、实际文本回传和日志安全边界
- [ ] 4.2 运行 `openspec validate enhance-ask-user-custom-input --strict`、完整测试和 Release 构建，确认无失败、无编译错误
- [ ] 4.3 发布桌面端到 `outputs/2026-09-20-<time>-ask-user-custom-input`，使用 `cua-driver` 验证单选、多选自由输入、互斥行为、默认提示和提交后的 AI 可见结果
