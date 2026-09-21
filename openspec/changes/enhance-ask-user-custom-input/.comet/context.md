# Comet Design Handoff

- Change: enhance-ask-user-custom-input
- Phase: design
- Mode: compact
- Context hash: a85b264386c9ca66b7f4f80e05eaa65713e837c171ce5e56f15799e2632b431e

Generated-by: comet-handoff.sh
Task hash policy: task-content-v1. Read tasks.md for live completion; excerpts are design-time context.

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/enhance-ask-user-custom-input/proposal.md

- Source: openspec/changes/enhance-ask-user-custom-input/proposal.md
- Lines: 1-33
- SHA256: 270d6c0605f47d4fb3fc27a952815676430b6c9fbc27ac44192f91d8d55c435c

```md
## Why

AskUser 的选择题只能返回预设 option id，用户遇到所有选项均不符合意图时无法直接补充真实想法；同时自由文本字段当前只向 AI 返回 `provided=true`，实际内容被丢弃，导致 AskUser 无法完成有效澄清。

## What Changes

- 为 `single_select` 与 `multi_select` 字段增加可选的自由输入能力，并允许调用方提供输入框占位提示。
- 在选择题选项下方以“我有其他想法...”作为默认占位提示展示输入框；自由输入与预设选项可以同时保留和提交。
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
- 桌面交互：AskUser 字段 ViewModel、悬浮卡片选择题模板及键盘/选择与自由输入共存行为。
- 测试：AssistantTools、UserDecision、AskUser ViewModel 与视图契约测试。
- Agent 循环：AskUser 取消后本轮移除工具可见性，并对模型的重复调用复用取消结果。
- 不引入新依赖，不修改数据库 Schema，不记录用户输入到日志。

```

## openspec/changes/enhance-ask-user-custom-input/design.md

- Source: openspec/changes/enhance-ask-user-custom-input/design.md
- Lines: 1-80
- SHA256: b1939b29ff29a5e14634ef6d56b1f3cf093aa3b234b8d2e1cd9b214ad162771f

```md
﻿## Context

当前 AskUser 请求模型把单选、多选、数字和文本统一表示为 `UserDecisionField`，提交时仅传递字段值字典。选择字段没有承载自由输入的状态，文本结果又在工具序列化阶段被转换为存在性标记。此次变更需要贯穿请求 Schema、领域校验、Broker 提交结果、卡片 ViewModel 和 Avalonia 模板，同时保持未启用自由输入的选择字段兼容。需求契约见 `specs/assistant-user-decisions/spec.md`。

## Goals / Non-Goals

**Goals:**

- 让单选和多选字段按需启用可与选项共存的自由输入。
- 保留 `values` 中既有 option id / option id 数组结构，通过独立 `custom_inputs` 返回自由输入。
- 让文本字段向 AI 返回实际内容，并确保内容不进入日志。
- 使用现有 ViewModel、Validator、Broker 和资源本地化模式完成最小扩展。

**Non-Goals:**

- 允许自由输入与预设选项同时提交，并分别保存在 `custom_inputs` 与 `values` 中。
- 不新增富文本、附件、多个自由输入项或持久化草稿。
- 不改变数字字段，也不改变未启用自由输入的选择题行为。
- 不把用户输入写入日志、Toast、会话标题或诊断摘要。

## Decisions

### 1. 在选择字段契约上增加显式开关和占位提示

`UserDecisionField` 与工具 Schema 增加 `allow_custom_input` 和 `custom_input_placeholder`。开关默认 false；占位提示仅在开关启用时生效。长度限制复用字段已有 `max_length`：未指定时使用现有默认值 1000，最大 4000，避免再引入一组重复限制属性。默认占位提示通过本地化资源提供，中文为“我有其他想法...”。

备选方案是在每个选项列表中自动插入一个伪造的“其他” option。该方案会污染 option id、要求调用方识别特殊值，并无法自然承载用户原文，因此不采用。

### 2. ViewModel 分别保存结构化选择和自由输入

单选、多选字段 ViewModel 增加 `CustomInput`、`AllowsCustomInput`、`CustomInputPlaceholder` 和可见性状态。自由输入与现有选择共存；用户输入或选择/切换选项时均不得清空另一方。仅用户明确执行清空/跳过操作时同时清除两种状态。单选自动前进只由预设 RadioButton 点击触发，自由输入不会自动前进，避免用户尚未完成表达就切换题目。

选择与文字采用共存模型。应用不擅自推断二者冲突，也不因用户输入或选择操作清空另一方；助手同时接收 `values` 与 `custom_inputs`，结合上下文判断补充、修正或权重关系。

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

### 7. 明确模型可见的同页建模规则

`assistant.ask_user` 的工具描述、Schema 字段 description 和 AssistantService 系统提示必须明确：`fields` 中每个字段独立分页；当用户要求选择题选项下方同页输入时，只创建一个 `single_select` 或 `multi_select` 字段并设置 `allow_custom_input=true`，用户指定的输入长度写入同一字段的 `max_length`，不得额外创建 `text` 字段。该规则属于工具调用契约，不改变运行时 UI 或结果结构。
## Risks / Trade-offs

- [工具结果开始包含用户实际文本，模型上下文敏感度提高] → 仅把内容返回发起 AskUser 的当前工具调用；日志、SafeArguments、Toast 和诊断信息继续只使用安全摘要。
- [Broker 接口扩展影响测试替身和调用点] → 保持单一提交入口并更新全部实现/替身，使用编译错误和定向测试覆盖遗漏。
- [单选切换仍可能因双向绑定产生递归通知] → 在选择字段 ViewModel 内保留现有更新保护标记，仅维护单选选项之间的互斥，不修改自由输入或多选状态。
- [默认占位提示在不同语言中长度不同] → 使用资源本地化和 TextBox watermark，不为提示预留固定宽度。

## Migration Plan

1. 先扩展模型、Schema 和校验，保留 `allow_custom_input=false` 默认值。
2. 再扩展 Broker 结果和工具序列化；现有请求无需修改即可继续工作。
3. 最后接入卡片 ViewModel、Avalonia 模板和本地化资源。
4. 通过定向测试、完整测试、Release 构建和桌面验收后发布新的时间戳输出目录。

回滚时可整体恢复本 change；数据库和持久化格式未变化，无需数据迁移。

### 8. AskUser 取消后在当前 AgentLoop 内禁用重复面板

卡片关闭按钮现有链路已能通过 `AskUserDialogViewModel.TryCancel`、`AssistantViewModel.CancelOwnedUserDecision` 和 `UserDecisionBroker.Cancel` 生成 `cancelled=true`。缺陷发生在结果返回后：AgentLoop 把取消结果当作普通成功工具结果，并在下一次模型请求中继续公开 `assistant.ask_user`，模型因此可以重新调用并产生第二张卡片。

AgentLoop 在检测到成功的 AskUser 取消结果后，按工具名保存该结构化结果，并在本轮后续模型请求的工具列表中移除 `assistant.ask_user`。若上游模型忽略工具列表仍生成重复调用，AgentLoop 直接复用已保存的取消结果，不执行 Handler、不进入 Broker、不再次展示 UI。会话本身不立即终止，模型仍可读取 `cancelled=true` 并生成最终取消摘要。禁用状态只存在于单次 `RunAsync`，下一轮用户消息仍可正常使用 AskUser。

该方案比直接取消整个 Assistant 轮次更符合既有“助手收到结构化取消结果并恢复原流程”的契约；同时比仅增加提示词更可靠，因为重复调用在运行时边界被确定性拦截。

```

## openspec/changes/enhance-ask-user-custom-input/tasks.md

- Source: openspec/changes/enhance-ask-user-custom-input/tasks.md
- Lines: 1-26
- SHA256: a0845d54f31c78d26ae1b7d7798fd5825e333870c835f71d574216c4fed440e1

```md
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

```

## openspec/changes/enhance-ask-user-custom-input/.openspec.yaml

- Source: openspec/changes/enhance-ask-user-custom-input/.openspec.yaml
- Lines: 1-2
- SHA256: 38b9a21742cd7082f58c6fd05e35c7787002fc92efecda464d1d11ad04b8cc65

```md
schema: spec-driven
created: 2026-09-21

```

## openspec/changes/enhance-ask-user-custom-input/specs/assistant-user-decisions/spec.md

- Source: openspec/changes/enhance-ask-user-custom-input/specs/assistant-user-decisions/spec.md
- Lines: 1-70
- SHA256: 665b27f2306ef37a8dc89bd2c60630433b1009c1851af81afb6518e468eb0cb7

```md
﻿## ADDED Requirements

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

```
