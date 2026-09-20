# enhance-ask-user-custom-input 验证报告

- Change：`enhance-ask-user-custom-input`
- 验证日期：2026-09-20
- 验证模式：`full`
- 基线：`e364174ae800c15d97106d3b5f8b90ddbf34d823`
- 验证分支：`codex/enhance-ask-user-custom-input`

## 结论

**PASS，可以进入归档确认阶段。**

本次完整验证未发现 CRITICAL 或 WARNING 级别问题。11/11 个实施任务均已完成，2 项需求、11 个规格场景均有实现与自动化测试或桌面验收证据；实现与 OpenSpec design、Superpowers Design Doc 及 proposal 一致，未发现 spec 漂移。

## 验证总览

| 维度 | 结果 | 证据 |
|---|---|---|
| 完整性 | PASS | `tasks.md` 11/11 完成；OpenSpec 状态 `all_done` |
| 正确性 | PASS | AskUser 定向测试 187/187；Release 全量测试 1110/1110 |
| 一致性 | PASS | OpenSpec strict validate 通过；实现符合两份设计文档与 delta spec |
| 构建 | PASS | Release build 0 error |
| 桌面交互 | PASS | 时间戳发布包通过 CUA 验收 |
| 安全边界 | PASS | 日志回归测试确认用户文本不进入格式化消息、结构化 state 或异常文本 |
| 审查 | PASS | `standard` 本地集成审查无 CRITICAL/IMPORTANT 发现 |

## 1. 任务完成度

OpenSpec `instructions apply` 返回：

- 总任务：11
- 已完成：11
- 未完成：0
- 状态：`all_done`

任务权威文件：`openspec/changes/enhance-ask-user-custom-input/tasks.md`。

## 2. 需求与场景覆盖

### 2.1 AskUser 选择字段支持可选自由输入

| 规格场景 | 实现证据 | 测试/验收证据 | 结果 |
|---|---|---|---|
| 选择字段展示自由输入框 | `LoomX/Views/AskUserCard.axaml:59-63,84-88` | `AskUserDialogContractTests`、`AssistantViewStyleTests`；CUA 实际显示 | PASS |
| 未启用时保持原有界面 | `IsVisible="{Binding AllowsCustomInput}"` | 视图契约测试覆盖可见条件 | PASS |
| 输入自由内容清除已有选择 | `AskUserDialogViewModel.cs` 的单选/多选 `CustomInput` setter | `单选自由输入_展示默认提示并与预设选项互斥`、`多选自由输入_与预设项互斥并投影到独立映射`；CUA 验收 | PASS |
| 重新选择选项清空自由输入 | 选择项变更回调清空 `CustomInput` | 同上；CUA 验收 | PASS |
| 自由输入满足必填选择题 | `UserDecisionValidator.ValidateSubmission` 同时校验 `values` 与 `customInputs` | `提交校验_选择题自由输入满足必填并拒绝冲突或未授权输入`；CUA 提交必填单选/多选 | PASS |
| 空白自由输入不满足校验 | `string.IsNullOrWhiteSpace` 判定 | `提交校验_拒绝空白超长与未知自由输入` | PASS |

### 2.2 AskUser 返回用户输入原文

| 规格场景 | 实现证据 | 测试证据 | 结果 |
|---|---|---|---|
| 文本字段返回实际内容 | `AssistantTools.SerializeResult` 直接序列化 `result.Values` | `AssistantToolsTests`、`AssistantServiceTests` | PASS |
| 单选自由输入返回实际内容 | `values` 保持 `null`，自由文本进入 `CustomInputs` | `AskUser_选择题自由输入返回CustomInputs原文`、ViewModel/Broker 测试 | PASS |
| 多选自由输入返回实际内容 | `values` 保持空只读数组，文本进入 `CustomInputs` | ViewModel、Validator 与工具结果测试 | PASS |
| 预设选择保持兼容 | 自由输入与 option 互斥；`values` 结构未改 | 现有完整回归测试与新增兼容断言 | PASS |
| 用户输入不进入日志 | Broker 成功日志只记录 `RequestId`、`FieldCount`、`CustomInputCount` | `日志_不包含普通文本与选择题自由输入原文` | PASS |

## 3. 设计一致性

### OpenSpec design 决策

1. **显式开关与占位提示**：已实现 `AllowCustomInput`、`CustomInputPlaceholder`，Schema 暴露 `allow_custom_input` 与 `custom_input_placeholder`。
2. **结构化选择与自由输入分离**：ViewModel 保存独立 `CustomInput`，并在选择/文字之间执行互斥清理。
3. **双映射提交校验**：Validator、Broker、ViewModel 和 AssistantViewModel 均传递 `values + customInputs`。
4. **不可变结果快照**：`UserDecisionResult` 对 `Values` 和 `CustomInputs` 分别复制为只读映射。
5. **兼容结果契约**：根对象固定包含 `custom_inputs`，普通 text 直接返回字符串，选择字段的既有值类型保持不变。
6. **自然 UI 文案**：单选、多选模板仅展示 Watermark，不显示“其他（可选）”标签；四语资源已补齐。

### Superpowers Design Doc

`docs/superpowers/specs/2026-09-20-ask-user-custom-input-design.md` 可定位，组件边界、数据契约、互斥状态机、日志安全、键盘行为与实际实现一致。未发现 delta spec 已改变但 Design Doc 未同步的情况。

## 4. 自动化验证

### OpenSpec

```text
node .../comet-runtime.mjs openspec -- validate enhance-ask-user-custom-input --strict
Change 'enhance-ask-user-custom-input' is valid
```

### AskUser 定向测试

```text
已通过! - 失败: 0，通过: 187，已跳过: 0，总计: 187
```

覆盖 `UserDecision`、`AskUser`、`AssistantToolsTests`、`AssistantServiceTests`、`AssistantViewModelTests`。

### Release 全量测试

Comet verify evidence：

```text
openspec/changes/enhance-ask-user-custom-input/.comet/checks/2b80bb6f-a004-4ea5-b2e5-d4a1439edd1b.log
已通过! - 失败: 0，通过: 1110，已跳过: 0，总计: 1110
```

### Release 构建

```text
已成功生成。
7 个警告
0 个错误
```

这些警告均为仓库已有基线问题，不是本 change 引入：

- `SQLitePCLRaw.lib.e_sqlite3 2.1.11`：NU1903
- `SettingsViewModel.cs`：CS8618
- `AnthropicResponseMapper.cs`：CA2024
- `AnthropicRequestFactoryTests.cs`：CS8602

## 5. 桌面发布与 CUA 验收

交付发布包：

```text
outputs/2026-09-20-043915-ask-user-custom-input/LoomX.exe
```

验收结果：

- Watermark 显示“我有其他想法...”。
- 界面未显示“其他（可选）”。
- 单选、多选的预设选项与自由输入互斥。
- 重新选择单选 option 会清空自由输入，并沿用既有自动前进逻辑。
- Previous / Next 后自由输入保持。
- 必填单选和多选均可由自由输入满足并提交。
- 发布进程 Path 已确认来自上述时间戳目录。

另有一个由主机时钟生成的 `outputs/2026-09-21-043207-ask-user-custom-input` 目录；按多会话产物边界未删除，也不作为本次正式交付路径。

## 6. 已知外部限制

桌面验收提交后，上游模型返回非标准空响应：

```text
模型请求失败。可能是模型返回了非标准响应，请重试或更换模型。
服务描述：Responses 流没有文本或工具调用。
```

该问题发生在工具结果已提交后的上游模型响应阶段，不属于 AskUser 数据链路缺陷。`AssistantServiceTests` 已自动验证包含实际 text 与 `custom_inputs` 的工具结果会进入下一轮模型请求，因此不阻塞本 change。

## 7. 审查问题分级

### CRITICAL

无。

### WARNING

无。

### SUGGESTION

- `git diff --check` 报告 3 个文件存在尾部空行；属于格式清理项，不影响编译、测试、运行时行为或规格一致性，且 Verify 阶段不修改实现/测试/Design Doc，因此留待后续常规整理。

## 最终评估

所有必需检查通过，未发现阻止归档的问题。Change 已准备好进入 archive 阶段，等待用户明确授权后执行归档。