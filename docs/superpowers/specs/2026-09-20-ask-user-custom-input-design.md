---
comet_change: enhance-ask-user-custom-input
role: technical-design
canonical_spec: openspec
---

# LoomX AskUser 选择题自由输入与真实文本回传设计

## 1. 背景与约束

AskUser 当前通过 `AssistantTools` 解析工具参数，构造 `UserDecisionRequest`，再由 `UserDecisionBroker` 挂起请求。桌面端 `AssistantViewModel` 领取请求并创建 `AskUserDialogViewModel`，用户提交后仅把 `IReadOnlyDictionary<string, object?> values` 传回 Broker。最终 `AssistantTools.SerializeResult` 会把文本字段替换为 `{ "provided": true }`，造成真实文本丢失。

本次设计必须满足 OpenSpec delta spec，并遵守以下项目约束：

- 不改变 AskUser 的 Claim、取消、所有权和生命周期语义。
- 未启用自由输入的选择字段保持现有参数与结果兼容。
- 用户输入只能进入当前工具结果，不能进入日志、Toast、错误摘要或 SafeArguments。
- 使用现有 Avalonia MVVM、本地化资源和结构化日志模式，不引入新依赖。

## 2. 组件边界

```text
assistant.ask_user JSON
        │
        ▼
AssistantTools.ParseRequest
        │ UserDecisionField + custom-input metadata
        ▼
UserDecisionBroker.RequestAsync
        │ PendingUserDecision
        ▼
AssistantViewModel → AskUserDialogViewModel → AskUserCard
        │ values + customInputs
        ▼
UserDecisionBroker.Submit
        │ UserDecisionResult
        ▼
AssistantTools.SerializeResult
        │
        ▼
{ cancelled, values, custom_inputs }
```

- `AssistantTools`：公开 Schema、参数解析、安全参数投影和工具结果序列化。
- `UserDecisionModels`：字段元数据、请求/提交校验和不可变结果快照。
- `UserDecisionBroker`：提交两个映射并维持并发完成语义。
- `AskUserDialogViewModel`：字段状态、选择与自由输入共存、分页校验和结果投影。
- `AskUserCard`：单选/多选模板中的自由输入 TextBox，不处理业务校验。
- `AssistantViewModel`：只负责把 ViewModel 构建的两个映射提交给 Broker。

## 3. 请求契约

### 3.1 新增字段属性

`UserDecisionField` 增加：

```csharp
public bool AllowCustomInput { get; }
public string? CustomInputPlaceholder { get; }
```

构造函数在 `maxLength` 之前增加可选参数或以命名参数接入，调用点必须使用命名参数避免位置错位。公开 JSON Schema 增加：

```json
{
  "allow_custom_input": { "type": "boolean", "default": false },
  "custom_input_placeholder": { "type": "string", "maxLength": 200 }
}
```

`max_length` 对 text 字段和启用自由输入的选择字段生效，默认 1000，最大 4000。`custom_input_placeholder` 只允许用于 `single_select` / `multi_select` 且 `allow_custom_input=true`；其他字段或未启用开关时提供该属性均视为不适用属性。

### 3.2 SafeArguments

安全参数投影可增加 `custom_input_allowed` 布尔摘要，但不得包含 placeholder、默认文本或用户答案。这样日志和审批摘要仍只能看到能力形态，不能看到内容。

## 4. 提交与结果契约

### 4.1 两个结果映射

提交链路使用两个独立映射：

```csharp
IReadOnlyDictionary<string, object?> values
IReadOnlyDictionary<string, string> customInputs
```

`values` 保持现有类型：

| 字段类型 | 预设答案 | 自由输入答案 |
|---|---|---|
| single_select | option id 字符串 | `null` |
| multi_select | option id 只读数组 | 空只读数组 |
| number | decimal | 不适用 |
| text | 实际字符串 | 不适用 |

`customInputs` 只包含实际提交了非空自由输入的选择字段。所有键使用原字段 id，所有值保留用户原文，不主动 Trim；空白判断使用 `string.IsNullOrWhiteSpace`。

### 4.2 Broker 接口

`IUserDecisionBroker.Submit` 调整为：

```csharp
bool Submit(
    string requestId,
    string claimantId,
    IReadOnlyDictionary<string, object?> values,
    IReadOnlyDictionary<string, string>? customInputs = null);
```

Broker 将 null 视为空映射，并在 Claim 校验之后分别创建两个只读快照，再调用统一提交校验。快照失败、校验失败和并发完成都沿用现有安全日志，只记录 `RequestId`、错误类型或错误数量。成功日志可记录结构化字段数量，但不得记录键名和值。

`UserDecisionResult` 增加只读 `CustomInputs`，`Submit` 工厂同时接收两个映射；取消结果返回两个空映射。

### 4.3 工具结果

最终 JSON 固定为：

```json
{
  "cancelled": false,
  "values": {
    "mode": null,
    "features": [],
    "note": "用户输入的实际说明"
  },
  "custom_inputs": {
    "mode": "我实际想要的模式"
  }
}
```

预设选择继续使用原始 option id / option id 数组。`custom_inputs` 始终存在；未使用时为空对象，减少模型对可选根属性的分支判断。文本字段不再生成 `provided` 包装对象。

## 5. 校验规则

### 5.1 请求校验

- 只有 single_select / multi_select 可以设置 `AllowCustomInput=true`。
- `CustomInputPlaceholder` 必须非空白、长度不超过 200，并继续执行展示文本敏感模式检查。
- `MaxLength` 对 text 或启用自由输入的选择字段合法；范围为 1–4000。
- 选择字段仍必须提供至少一个 option，自由输入不是取消 options 要求的替代品。
- 现有默认选项、最少/最多选择数和字段类型属性规则保持不变。

### 5.2 提交校验

`ValidateSubmission` 增加 `customInputs` 参数，并保留旧重载转发空映射，降低测试和非 UI 调用迁移成本。处理顺序：

1. 拒绝 `values` 或 `customInputs` 中未知字段 id。
2. 对每个字段读取结构化值和自由输入。
3. 自由输入存在时，要求字段为已启用自由输入的选择字段。
4. 自由输入必须非空白且不超过字段 `MaxLength ?? 1000`。
5. 自由输入与预设值可以同时有效；有选择时 `values` 保留 option id，没有选择时保持 null 或空集合。
6. 合法自由输入直接满足 `is_required` 和 multi-select 的 `min_selections`。
7. 没有自由输入时完全复用现有值类型、option id、数量、数字和文本校验。

错误消息只描述类型、长度或状态，不拼接用户原文。

## 6. ViewModel 状态机

### 6.1 基类投影

`AskUserFieldViewModel` 增加：

```csharp
internal string? GetCustomInput()
protected virtual string? GetCustomInputCore() => null;
```

`AskUserDialogViewModel.BuildCustomInputs()` 收集非空自由输入，并在 `TryBuildResult` 中同时返回两个只读映射。`FindCurrentError` 和最终校验统一传入两个映射，避免 UI 校验与 Broker 校验漂移。

### 6.2 单选字段

新增属性：

```csharp
public bool AllowsCustomInput { get; }
public string CustomInputPlaceholder { get; }
public int CustomInputMaxLength { get; }
public string CustomInput { get; set; }
```

状态转换：

- `CustomInput` 从空白变为非空时，在更新保护区内取消所有 RadioButton。
- option 变为选中时，在更新保护区内把 `CustomInput` 设为空字符串。
- 取消 RadioButton 本身不恢复旧文本。
- `ClearValueCore` 同时清空选项和自由输入。
- 自由输入变化只触发校验，不触发 `SingleChoice_OnClick` 的延迟自动前进。

### 6.3 多选字段

多选使用同一组公开属性。`CustomInput` 变为非空时保留已有 CheckBox；任一 CheckBox 变为选中或取消时均保留自由输入。所有批量变更使用单一更新保护标记，最终只触发一次值变化通知。

## 7. Avalonia UI 与本地化

单选和多选 DataTemplate 在 `ItemsControl` 后增加：

```xml
<TextBox Text="{Binding CustomInput, Mode=TwoWay}"
         Watermark="{Binding CustomInputPlaceholder}"
         MaxLength="{Binding CustomInputMaxLength}"
         IsVisible="{Binding AllowsCustomInput}"
         KeyDown="SelectionCustomInput_OnKeyDown"/>
```

不增加 TextBlock 标签。中文资源 `assistant.decision.custom_input_placeholder` 为“我有其他想法...”；英文、日文、繁中使用自然等价表达。调用方提供 `custom_input_placeholder` 时优先使用调用方值，否则由 ViewModel 解析资源。

`SelectionCustomInput_OnKeyDown` 采用普通单行文本行为：Enter 调用 `AdvanceOrSubmit()`，Ctrl+Enter 不作特殊处理。输入框使用现有主题资源和默认 TextBox 样式，避免新增独立视觉组件。

## 8. 日志与隐私边界

- `AssistantTools` 不记录工具结果正文。
- Broker 成功日志只记录 `RequestId`、字段数量和自由输入字段数量。
- 校验 Warning 只记录错误数量。
- `AssistantViewModel` 继续只记录 ResultKind、RequestId 和提交状态。
- 测试必须使用可识别的秘密样例，断言日志捕获器中不存在文本字段和 custom input 原文。

## 9. 主要文件变更

- `LoomX/Assistant/UserDecisions/UserDecisionModels.cs`：字段元数据、双映射校验、结果快照。
- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs`：Submit 签名与双快照提交。
- `LoomX/Assistant/AssistantTools.cs`：Schema、解析、安全投影和结果 JSON。
- `LoomX/ViewModels/AskUserDialogViewModel.cs`：自由输入状态、选择共存和双映射投影。
- `LoomX/ViewModels/AssistantViewModel.cs`：向 Broker 提交 customInputs。
- `LoomX/Views/AskUserCard.axaml(.cs)`：选择题输入框和 Enter 行为。
- `LoomX/Resources/Strings*.resx`：默认 watermark。
- `LoomX.Tests/Assistant/UserDecisionModelsTests.cs`、`AssistantToolsTests.cs`、`AssistantServiceTests.cs`、`AssistantViewModelTests.cs`：领域与集成回归。
- `LoomX.Tests/Views/AskUserDialogContractTests.cs`、`AssistantViewStyleTests.cs`：ViewModel、XAML 和本地化契约。

### 7. 模型调用契约必须表达分页与同页输入

实现不仅要渲染选择字段下方的自由输入框，还必须让模型知道如何构造请求：`fields` 中每个字段独立分页；用户要求单选/多选选项下方同页输入时，使用同一个选择字段的 `allow_custom_input=true`，并把“输入框80字”等长度要求写入该字段的 `max_length`，不要创建独立 `text` 字段。该说明同时放入工具描述、Schema description 和系统提示，避免模型将同页输入误建模为下一页字段。
## 10. TDD 与验证策略

实施分四个红绿循环：

1. 请求 Schema 与字段模型：先证明新属性不存在或被拒绝，再实现解析和请求校验。
2. 提交与结果：先证明自由输入不能满足校验、Broker 无法保存、文本仍被替换，再实现双映射。
3. ViewModel 与 XAML：先证明没有自由输入状态和模板，再实现选择与文字共存、分页保留和本地化。
4. 集成安全：验证 AssistantService 收到实际 JSON，日志捕获器不含用户原文。

最终验证包括：

- 相关测试项目的定向 `dotnet test`。
- 完整 `dotnet test`。
- `dotnet build -c Release`。
- `openspec validate enhance-ask-user-custom-input --strict`。
- 发布到 `outputs/2026-09-20-<time>-ask-user-custom-input`。
- 通过 `cua-driver` 验证单选、多选、选择与文字共存、Enter 提交和默认 watermark。

## 11. 完成判定

- 未启用自由输入的选择题行为和工具结果不变。
- 启用后输入框只显示 watermark“我有其他想法...”，无生硬额外标签。
- 选择与文字可同时保留并提交，必填规则正确。
- AI 同时收到实际 text 字符串和选择题 `custom_inputs` 原文。
- 所有日志和用户可见诊断都不包含输入原文。
- 完整测试、Release 构建、OpenSpec strict validate、发布和桌面验收均通过。


## 9. AskUser 取消终态与重复调用屏蔽

### 9.1 根因

右上角关闭按钮本身已正确完成 ViewModel 与 Broker 取消，工具结果也是 `{"cancelled":true,"values":{},"custom_inputs":{}}`。问题在 AgentLoop：该结果被当作普通成功结果后，下一轮模型请求仍携带 `assistant.ask_user` 工具定义。模型再次调用同一工具时会创建新的 Pending 请求，所以用户看到卡片关闭后又弹出；若第二张卡片最终提交，助手看到的最后结果就会变成 `cancelled=false`。

### 9.2 运行时约束

单次 `AgentLoop.RunAsync` 维护仅限本轮的禁用工具结果映射。首次 AskUser 返回 `cancelled=true` 后：

1. 将原结构化取消结果按 `assistant.ask_user` 保存；
2. 后续模型请求不再公开该工具；
3. 若模型仍生成重复调用，直接写入保存的取消结果，不执行 Handler，因此不会再次进入 Broker 或 UI；
4. AgentLoop 继续运行，让模型能够准确总结“面板已取消（cancelled: true）”；
5. 新的用户轮次重新创建映射，AskUser 能力恢复。

该边界以运行时拦截为主，不依赖模型遵守提示词，并保持既有“取消面板不等于停止整个助手轮次”的语义。
