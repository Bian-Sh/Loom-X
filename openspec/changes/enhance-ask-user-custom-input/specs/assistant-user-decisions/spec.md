## ADDED Requirements

### Requirement: AskUser 选择字段必须支持可选自由输入
系统 SHALL 允许 `single_select` 和 `multi_select` 字段通过 `allow_custom_input` 启用自由输入，并 SHALL 允许调用方通过 `custom_input_placeholder` 自定义占位提示。未提供占位提示时，桌面端 SHALL 使用“我有其他想法...”作为默认提示。自由输入与预设选项 SHALL 互斥，且未启用该能力的选择字段 SHALL 保持现有交互和结果结构。

#### Scenario: 选择字段展示自由输入框
- **WHEN** 单选或多选字段设置 `allow_custom_input=true`
- **THEN** 桌面端在选项列表下方展示自由输入框，并使用调用方指定的占位提示或默认的“我有其他想法...”

#### Scenario: 未启用时保持原有界面
- **WHEN** 选择字段未设置 `allow_custom_input` 或其值为 false
- **THEN** 桌面端不展示自由输入框，字段继续只接受预设选项

#### Scenario: 输入自由内容清除已有选择
- **WHEN** 用户在选择字段的自由输入框中输入非空内容
- **THEN** 系统清除该字段已选中的单选或多选 option id，并保留用户输入原文

#### Scenario: 重新选择选项清空自由输入
- **WHEN** 用户已输入自由内容后重新选择任一预设选项
- **THEN** 系统清空该字段的自由输入，并按既有单选或多选规则保存 option id

#### Scenario: 自由输入满足必填选择题
- **WHEN** 必填选择字段没有有效预设选项但包含非空自由输入
- **THEN** 字段校验通过；多选字段的 `min_selections` 约束不阻止该自由输入作为替代答案提交

#### Scenario: 空白自由输入不满足校验
- **WHEN** 必填选择字段仅包含空白自由输入且没有有效预设选项
- **THEN** 系统按缺少有效值处理并显示安全校验摘要

### Requirement: AskUser 必须把用户输入原文返回给助手
系统 SHALL 在 AskUser 成功提交时把文本字段的实际字符串写入 `values`，不得仅返回输入存在性标记。选择字段使用自由输入时，系统 SHALL 在结果根对象的 `custom_inputs` 映射中按字段 id 返回用户原文，同时在 `values` 中保留该选择字段现有类型对应的空值。系统 MUST NOT 因返回原文而把用户输入记录到运行日志。

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
