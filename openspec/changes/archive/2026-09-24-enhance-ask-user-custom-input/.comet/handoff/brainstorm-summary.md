# Brainstorm Summary

- Change: enhance-ask-user-custom-input
- Date: 2026-09-20

## 确认的技术方案

- 在 `UserDecisionField`、AskUser Schema 与解析器中增加 `allow_custom_input`、`custom_input_placeholder`；选择字段复用 `max_length` 控制自由输入长度。
- `single_select`、`multi_select` ViewModel 各自保存 `CustomInput`，自由输入与 option 选择互斥；自由输入不会触发单选自动前进。
- 提交结果显式拆分为 `Values` 与 `CustomInputs` 两个只读映射；自由输入单选在 `Values` 中为 null，多选为空数组，原文位于 `CustomInputs`。
- `UserDecisionValidator` 同时校验两个映射；有效自由输入可替代必填与 `min_selections`，未授权、空白、超长输入均拒绝。
- `AssistantTools.SerializeResult` 直接返回文本字段字符串，并在根对象输出 `custom_inputs`；既有 option id 结果保持兼容。
- Avalonia 模板只显示带 watermark 的输入框，不显示“其他（可选）”标签；中文默认 watermark 为“我有其他想法...”，其他语言提供自然等价文案。

## 关键取舍与风险

- 使用独立 `custom_inputs` 而不是特殊 option id 或联合值对象，避免破坏 `values` 的既有类型。
- 自由输入与选项互斥，避免 AI 无法判断二者优先级；需要防止双向绑定递归通知。
- 文本原文进入工具结果后可能包含敏感用户内容，因此只返回当前模型调用，禁止进入日志、SafeArguments、Toast 或错误摘要。
- Broker 接口签名变化会影响测试替身，必须通过编译、Broker 测试和 Assistant 生命周期测试完整覆盖。

## 测试策略

- 按 TDD 分别覆盖 Schema/解析、请求校验、提交校验、Broker 快照、工具序列化、ViewModel 互斥状态和 XAML/本地化契约。
- 每组实现前先运行新增测试确认因能力缺失而失败，再写最小实现并回归相关测试。
- 集成阶段运行 OpenSpec strict validate、完整测试、Release 构建、时间戳发布和 `cua-driver` 桌面验收。

## Spec Patch

无。现有 delta spec 已覆盖显示条件、互斥行为、必填替代、实际文本回传、兼容性与日志安全。
