# enhance-ask-user-custom-input 验证报告

- Change：`enhance-ask-user-custom-input`
- 验证日期：2026-09-20
- 验证模式：`full`
- 基线：`e364174ae800c15d97106d3b5f8b90ddbf34d823`
- 验证分支：`codex/enhance-ask-user-custom-input`

## 结论

**针对本 change 的验证通过，可以再次进入归档确认阶段。**

本轮针对用户反馈补齐了模型调用契约：工具描述、Schema description 与 AssistantService 系统提示现在明确“每个 fields 字段独立分页；选择题选项下方同页输入必须使用同一个选择字段的 `allow_custom_input=true`；输入长度写入同字段 `max_length`；不得新增独立 `text` 字段”。因此用户要求“单选和输入框在同一个弹窗、不换页、输入框80字”时，模型有明确的参数建模依据。

## 验证总览

| 维度 | 结果 | 证据 |
|---|---|---|
| 任务完整性 | PASS | OpenSpec `tasks.md` 12/12 完成，计划映射有效 |
| 针对性测试 | PASS | AskUser 相关测试 189/189 通过 |
| OpenSpec | PASS | strict validate 通过 |
| Release 构建 | PASS | `dotnet build LoomX.slnx -c Release --no-restore`，0 error |
| 发布包 | PASS | 新发布目录存在 `LoomX.exe` |
| UI/结果回归 | PASS | 原有选择题自由输入、文本回传与日志安全测试继续通过 |

## 1. 本轮根因与修复

### 根因

之前只实现了运行时 UI 和结果链路：当模型自行把用户请求理解为“一个选择字段 + 一个 text 字段”时，UI 会按既有分页模型将两个字段分别显示在两页。工具契约没有明确告诉模型“同页输入必须是选择字段的自由输入”，所以截图中的目标布局无法稳定由自然语言请求触发。

### 修复

1. `assistant.ask_user` 工具描述补充字段分页规则与同页自由输入建模规则。
2. `fields` Schema 增加说明：每个字段独立分页；同页输入不得新增 `text` 字段。
3. `allow_custom_input` Schema description 明确其用途、同页位置和禁止拆字段规则。
4. `custom_input_placeholder` 明确为选择题同页输入框 Watermark。
5. `max_length` 明确适用于启用自由输入的选择字段，并以“输入框80字”作为模型可见示例。
6. AssistantService 系统提示同步上述规则。
7. 新增工具契约测试与系统提示测试，先验证失败，再实现并转绿。

## 2. 需求与场景覆盖

### 2.1 AskUser 选择字段自由输入

OpenSpec 当前覆盖 7 个场景：展示输入框、同页单字段建模、未启用时保持原界面、输入清除选项、重新选择清空输入、自由输入满足必填、空白输入不满足校验。相关 ViewModel、Validator、XAML 和 CUA 证据均保留并通过。

### 2.2 AskUser 返回输入原文

OpenSpec 当前覆盖 5 个场景：普通文本实际字符串、单选自由输入、 多选自由输入、预设选择兼容、用户输入不进入日志。相关 Broker、工具序列化、AssistantService 与日志回归测试均通过。

## 3. 自动化验证

### 定向测试

```text
dotnet test LoomX.Tests\\LoomX.Tests.csproj -c Release --no-restore
  --filter "FullyQualifiedName~UserDecision|FullyQualifiedName~AskUser|FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AssistantViewModelTests"

已通过! - 失败: 0，通过: 189，已跳过: 0，总计: 189
```

Comet build 与 verify evidence 均已记录；verify evidence 日志：

```text
openspec/changes/enhance-ask-user-custom-input/.comet/checks/1e71bc0c-9eb7-4c85-9e60-8ec23426bfa2.log
```

### OpenSpec strict validate

```text
node .../comet-runtime.mjs openspec -- validate enhance-ask-user-custom-input --strict
Change 'enhance-ask-user-custom-input' is valid
```

### Release 构建

```text
dotnet build LoomX.slnx -c Release --no-restore
已成功生成。
0 个错误
```

仓库已有警告仍存在：NU1903、SettingsViewModel.cs CS8618、AnthropicResponseMapper.cs CA2024，以及测试中的 CS8602；本轮未新增这些警告。

## 4. 全量测试环境说明

本轮直接运行完整测试时，当前测试套件存在与本 change 无关的环境/顺序问题：

- 并行运行出现 Avalonia `Call from invalid thread`，涉及既有 NodeGraph/View 测试。
- 禁用并行后有 2 个既有 `ProviderEditorViewModelTests` 文化状态断言失败；单独运行该类 24/24 通过。
- 排除该既有测试类后，其余 1088/1088 通过。

AskUser 相关测试 189/189 通过，且失败堆栈不涉及本轮修改的工具描述、Schema 或系统提示。因此这些全局测试环境问题不阻塞本 change；没有修改无关测试基础设施。

## 5. 发布包

新发布包：

```text
outputs/2026-09-20-055155-ask-user-custom-input-model-guidance/LoomX.exe
```

已确认 `LoomX.exe` 存在。旧发布目录和另一个 Session 产物均未删除。

## 6. 桌面与交互验收依据

已有 CUA 验收继续证明：

- 选择题选项下方显示 Watermark“我有其他想法...”。
- 不显示“其他（可选）”标签。
- 自由输入与预设 option 互斥。
- Previous / Next 保留输入状态。
- 必填单选、多选可由自由输入满足。
- 普通 text 与 `custom_inputs` 原文进入后续 AI 工具结果。

本轮修复的重点是模型如何生成调用参数，而不是改变该已通过的 UI 布局。

## 7. 外部限制

此前桌面提交后上游模型返回非标准空响应：

```text
模型请求失败。可能是模型返回了非标准响应，请重试或更换模型。
服务描述：Responses 流没有文本或工具调用。
```

该问题发生在工具结果提交后的上游模型响应阶段，不属于 AskUser 参数建模或数据链路缺陷；AssistantServiceTests 已覆盖工具结果进入下一轮模型请求。

## 8. 问题分级

### CRITICAL

无。

### WARNING

无（全局测试套件的既有环境/顺序问题已在第 4 节单独记录，未纳入本 change 的功能缺陷）。

### SUGGESTION

无新增建议。

## 最终评估

本轮反馈对应的根因已定位并修复：模型现在能从工具契约中明确知道截图所示布局应使用单个选择字段的 `allow_custom_input`，而不是拆出下一页 `text` 字段。针对性测试、OpenSpec、Release 构建和发布包检查均通过，change 可以进入 archive 待用户确认阶段。