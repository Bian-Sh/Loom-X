## Context

当前 AskUser 请求模型把单选、多选、数字和文本统一表示为 `UserDecisionField`，提交时仅传递字段值字典。选择字段没有承载自由输入的状态，文本结果又在工具序列化阶段被转换为存在性标记。此次变更需要贯穿请求 Schema、领域校验、Broker 提交结果、卡片 ViewModel 和 Avalonia 模板，同时保持未启用自由输入的选择字段兼容。需求契约见 `specs/assistant-user-decisions/spec.md`。

## Goals / Non-Goals

**Goals:**

- 让单选和多选字段按需启用与选项互斥的自由输入。
- 保留 `values` 中既有 option id / option id 数组结构，通过独立 `custom_inputs` 返回自由输入。
- 让文本字段向 AI 返回实际内容，并确保内容不进入日志。
- 使用现有 ViewModel、Validator、Broker 和资源本地化模式完成最小扩展。

**Non-Goals:**

- 不允许自由输入与预设选项同时提交。
- 不新增富文本、附件、多个自由输入项或持久化草稿。
- 不改变数字字段，也不改变未启用自由输入的选择题行为。
- 不把用户输入写入日志、Toast、会话标题或诊断摘要。

## Decisions

### 1. 在选择字段契约上增加显式开关和占位提示

`UserDecisionField` 与工具 Schema 增加 `allow_custom_input` 和 `custom_input_placeholder`。开关默认 false；占位提示仅在开关启用时生效。长度限制复用字段已有 `max_length`：未指定时使用现有默认值 1000，最大 4000，避免再引入一组重复限制属性。默认占位提示通过本地化资源提供，中文为“我有其他想法...”。

备选方案是在每个选项列表中自动插入一个伪造的“其他” option。该方案会污染 option id、要求调用方识别特殊值，并无法自然承载用户原文，因此不采用。

### 2. ViewModel 分别保存结构化选择和自由输入

单选、多选字段 ViewModel 增加 `CustomInput`、`AllowsCustomInput`、`CustomInputPlaceholder` 和可见性状态。自由输入变为非空时清除现有选择；用户选择任一预设项时清空自由输入。清空/跳过操作同时清除两种状态。单选自动前进只由预设 RadioButton 点击触发，自由输入不会自动前进，避免用户尚未完成表达就切换题目。

备选方案是允许选择与文字共存。该方案会让必填、最少选择数和 AI 解释优先级变得含糊，也不符合“所有选项均不符合时输入其他想法”的目标，因此采用互斥模型。

### 3. 校验同时接收 values 与 customInputs

提交校验在既有字段值字典之外接收只包含非空文本的 `customInputs` 字典。选择字段启用自由输入时，非空且长度合法的自由输入可以替代选项满足 `is_required` 和 `min_selections`；`max_selections` 仍只约束 option id。未启用自由输入的字段如果出现对应 custom input，校验应拒绝，防止绕过请求契约。

字段请求校验负责限制占位提示和 `max_length` 的适用范围，并继续使用敏感字段模式检查展示文本；用户实际答案只做长度与空白校验，不把内容写入错误消息。

### 4. Broker 结果显式携带两类数据

扩展提交链路和 `UserDecisionResult`，同时保存只读的 `Values` 与 `CustomInputs`。Broker 在完成请求前复制并校验两个字典，维持现有并发、Claim 和取消语义。AskUser ViewModel 构造两个结果映射：选择自由输入时，单选值为 null、多选值为空数组，原文放入 `CustomInputs`。

备选方案是把自由输入伪装成 `values` 中的特殊对象。该方案会破坏现有值类型约定并迫使所有消费者解析联合类型，因此采用独立映射。

### 5. 工具结果保持向后兼容并修复文本丢失

`SerializeResult` 不再特殊隐藏 text 字段，统一把 `Values` 序列化为实际值，并新增 `custom_inputs` 对象。该对象只包含实际提交自由输入的字段；未使用时返回空对象。`SafeArgumentsProjector` 仍只输出字段类型、必填状态和选项数量等安全摘要，不投影默认文本、占位提示或用户答案。

### 6. UI 使用本地化默认提示并遵循现有卡片视觉

在单选和多选 DataTemplate 的选项列表下方复用现有 TextBox 风格，不增加“其他（可选）”标签。输入框仅通过占位提示传达用途；默认资源在各 Locale 中提供自然语言等价文案。输入内容参与现有 Continue / Submit 校验和 Previous / Next 值保留。

## Risks / Trade-offs

- [工具结果开始包含用户实际文本，模型上下文敏感度提高] → 仅把内容返回发起 AskUser 的当前工具调用；日志、SafeArguments、Toast 和诊断信息继续只使用安全摘要。
- [Broker 接口扩展影响测试替身和调用点] → 保持单一提交入口并更新全部实现/替身，使用编译错误和定向测试覆盖遗漏。
- [输入文字与选项互斥可能因双向绑定产生递归通知] → 在选择字段 ViewModel 内使用现有更新保护标记，集中执行状态切换并用单元测试覆盖。
- [默认占位提示在不同语言中长度不同] → 使用资源本地化和 TextBox watermark，不为提示预留固定宽度。

## Migration Plan

1. 先扩展模型、Schema 和校验，保留 `allow_custom_input=false` 默认值。
2. 再扩展 Broker 结果和工具序列化；现有请求无需修改即可继续工作。
3. 最后接入卡片 ViewModel、Avalonia 模板和本地化资源。
4. 通过定向测试、完整测试、Release 构建和桌面验收后发布新的时间戳输出目录。

回滚时可整体恢复本 change；数据库和持久化格式未变化，无需数据迁移。
