---
change: enhance-ask-user-custom-input
design-doc: docs/superpowers/specs/2026-09-20-ask-user-custom-input-design.md
base-ref: e364174ae800c15d97106d3b5f8b90ddbf34d823
---

<!-- comet-task-authority: openspec/changes/enhance-ask-user-custom-input/tasks.md -->
<!-- comet-task-ref:askuser-1-1 -->
<!-- comet-task-ref:askuser-1-2 -->
<!-- comet-task-ref:askuser-2-1 -->
<!-- comet-task-ref:askuser-2-2 -->
<!-- comet-task-ref:askuser-2-3 -->
<!-- comet-task-ref:askuser-3-1 -->
<!-- comet-task-ref:askuser-3-2 -->
<!-- comet-task-ref:askuser-3-3 -->
<!-- comet-task-ref:askuser-4-1 -->
<!-- comet-task-ref:askuser-4-2 -->
<!-- comet-task-ref:askuser-4-3 -->

# AskUser 选择题自由输入与真实文本回传实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. 任务完成状态以 OpenSpec `tasks.md` 为权威，本计划通过稳定 task ref 映射实施步骤。

**目标：** 为 AskUser 单选和多选字段增加可选的“我有其他想法...”自由输入，并把选择题自由输入与普通文本字段原文完整返回给 AI。

**架构：** 在现有 `UserDecisionField → UserDecisionBroker → AskUserDialogViewModel → AssistantTools.SerializeResult` 链路上增加独立的 `CustomInputs` 映射，不把自由输入伪装成 option id，也不改变既有 `values` 类型。UI 只在字段显式允许时显示 Watermark 输入框，ViewModel 负责选择与文字互斥，Validator 与 Broker 负责最终一致性和不可变快照。

**技术栈：** C# / .NET 10、Avalonia 11.3、xUnit、System.Text.Json、Microsoft.Extensions.Logging、OpenSpec / Comet。

**Spec：** `docs/superpowers/specs/2026-09-20-ask-user-custom-input-design.md`

## 全局约束

- 所有文档、代码注释和 git 提交消息使用中文；技术标识符保持英文。
- `allow_custom_input` 默认 false；未启用字段的 UI、校验和工具结果保持兼容。
- 中文默认 Watermark 必须是 `我有其他想法...`，不得显示“其他（可选）”标签。
- 自由输入与预设选择互斥；单选自由输入不触发自动前进。
- `values` 保持现有类型，选择题自由输入写入独立 `custom_inputs`。
- 文本字段实际字符串直接写入 `values`，不得再转换为 `{ "provided": true }`。
- 用户原文不得进入日志、Toast、SafeArguments 或错误摘要。
- 每个生产代码改动前必须先运行对应失败测试并确认失败原因正确。

---

### Task 1：扩展 AskUser 请求 Schema 与字段模型

**文件：**
- 修改：`LoomX/Assistant/UserDecisions/UserDecisionModels.cs`
- 修改：`LoomX/Assistant/AssistantTools.cs`
- 测试：`LoomX.Tests/Assistant/UserDecisionModelsTests.cs`
- 测试：`LoomX.Tests/Assistant/AssistantToolsTests.cs`

**接口：**
- 产生：`UserDecisionField.AllowCustomInput : bool`
- 产生：`UserDecisionField.CustomInputPlaceholder : string?`
- 产生：JSON 参数 `allow_custom_input`、`custom_input_placeholder`
- 保持：`max_length` 对 text 和启用自由输入的选择字段生效，范围 1–4000

- **Step 1：为 Schema 与解析增加失败测试**

在 `AssistantToolsTests` 增加 Schema 断言：

```csharp
[Fact]
public void RegisterAll_AskUser选择字段公开自由输入Schema()
{
    using var broker = CreateBroker();
    var tool = GetTool(broker);
    var fieldProperties = tool.ParametersSchema["properties"]!["fields"]!["items"]!["properties"]!.AsObject();

    Assert.Equal("boolean", fieldProperties["allow_custom_input"]!["type"]!.GetValue<string>());
    Assert.False(fieldProperties["allow_custom_input"]!["default"]!.GetValue<bool>());
    Assert.Equal(200, fieldProperties["custom_input_placeholder"]!["maxLength"]!.GetValue<int>());
}
```

增加一次真实工具调用测试，请求包含：

```json
{
  "id": "mode",
  "label": "运行模式",
  "type": "single_select",
  "allow_custom_input": true,
  "custom_input_placeholder": "描述你的模式",
  "max_length": 120,
  "options": [{ "id": "safe", "label": "安全模式" }]
}
```

在 Pending 请求断言 `AllowCustomInput == true`、placeholder 和 `MaxLength == 120`。

- **Step 2：运行测试确认红灯**

运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~UserDecisionModelsTests" --no-restore
```

预期：Schema 缺少两个属性，解析器因未知属性失败，模型属性不存在。

- **Step 3：实现字段模型、Schema 与解析**

在 `UserDecisionField` 构造函数和只读属性中加入：

```csharp
bool allowCustomInput = false,
string? customInputPlaceholder = null,
```

并赋值：

```csharp
AllowCustomInput = allowCustomInput;
CustomInputPlaceholder = customInputPlaceholder;
```

在 `AssistantTools.FieldProperties`、`ParseField` 和 `CreateAskUserSchema` 中加入对应 snake_case 属性。`SafeArgumentsProjector` 只增加布尔摘要：

```csharp
["custom_input_allowed"] = field?["allow_custom_input"] is JsonValue customInputValue
    && customInputValue.TryGetValue<bool>(out var allowed)
    && allowed,
```

不得投影 placeholder。

- **Step 4：实现请求属性适用性校验**

调整 `HasTextProperties` 与各字段类型属性检查，使规则为：

```csharp
private static bool HasCustomInputProperties(UserDecisionField field) =>
    field.AllowCustomInput || field.CustomInputPlaceholder is not null;
```

- single/multi：允许 `MaxLength`，但只有 `AllowCustomInput=true` 时允许 placeholder。
- number/text：拒绝 custom input 属性。
- text：继续允许 `DefaultText`、`IsMultiline`、`MaxLength`。
- 选择字段开启自由输入时校验 `MaxLength` 为 1–4000。
- placeholder 使用 `ValidateDisplayText(..., required: false, maxLength: 200)`，并拒绝纯空白值。

增加测试覆盖 number/text 误用、未开开关却提供 placeholder、超长 placeholder、合法选择字段 max length。

- **Step 5：运行定向测试确认绿灯**

运行与 Step 2 相同命令，预期全部通过。

- **Step 6：提交 Task 1**

```powershell
git add LoomX/Assistant/UserDecisions/UserDecisionModels.cs LoomX/Assistant/AssistantTools.cs LoomX.Tests/Assistant/UserDecisionModelsTests.cs LoomX.Tests/Assistant/AssistantToolsTests.cs
git commit -m "新增 AskUser 选择题自由输入契约"
```

### Task 2：实现双映射提交校验与 Broker 结果

**文件：**
- 修改：`LoomX/Assistant/UserDecisions/UserDecisionModels.cs`
- 修改：`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs`
- 修改：`LoomX.Tests/Assistant/UserDecisionBrokerTestExtensions.cs`
- 测试：`LoomX.Tests/Assistant/UserDecisionModelsTests.cs`
- 测试：`LoomX.Tests/Assistant/UserDecisionBrokerTests.cs`

**接口：**
- 产生：`UserDecisionValidator.ValidateSubmission(request, values, customInputs)`
- 产生：`UserDecisionResult.CustomInputs : IReadOnlyDictionary<string, string>`
- 修改：`IUserDecisionBroker.Submit(..., IReadOnlyDictionary<string, string>? customInputs = null)`
- 兼容：三参数 Submit 调用通过可选参数继续编译，测试扩展默认传空映射

- **Step 1：为提交规则增加失败测试**

在 `UserDecisionModelsTests` 增加以下独立用例：

```csharp
[Fact]
public void 提交校验_单选自由输入可满足必填()
{
    var request = CreateRequest(new UserDecisionField(
        "mode", "运行模式", UserDecisionFieldType.SingleSelect,
        isRequired: true,
        options: [new UserDecisionOption("safe", "安全模式")],
        allowCustomInput: true));

    var errors = UserDecisionValidator.ValidateSubmission(
        request,
        new Dictionary<string, object?> { ["mode"] = null },
        new Dictionary<string, string> { ["mode"] = "我想手动控制" });

    Assert.Empty(errors);
}
```

再覆盖：多选自由输入绕过 `min_selections`、空白输入失败、超长失败、未知字段失败、未授权字段失败、自由输入与选项同时存在失败。

- **Step 2：为 Broker 快照增加失败测试**

在 `UserDecisionBrokerTests` 提交：

```csharp
var customInputs = new Dictionary<string, string> { ["mode"] = "自定义模式" };
Assert.True(broker.Submit(request.RequestId, claimantId, values, customInputs));
customInputs["mode"] = "提交后篡改";
var result = await pendingTask;
Assert.Equal("自定义模式", result.CustomInputs["mode"]);
```

另加取消结果断言 `Values` 和 `CustomInputs` 都为空。

- **Step 3：运行测试确认红灯**

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~UserDecisionModelsTests|FullyQualifiedName~UserDecisionBrokerTests" --no-restore
```

预期：新重载、`CustomInputs` 和四参数 Submit 尚不存在。

- **Step 4：实现统一提交校验**

保留旧重载并转发空映射：

```csharp
public static IReadOnlyList<UserDecisionValidationError> ValidateSubmission(
    UserDecisionRequest request,
    IReadOnlyDictionary<string, object?> values) =>
    ValidateSubmission(request, values, EmptyCustomInputs);
```

新重载先校验两个映射的未知字段，再按字段执行：

```csharp
var hasCustomInput = customInputs.TryGetValue(field.Id, out var customInput)
    && !string.IsNullOrWhiteSpace(customInput);
```

有自由输入时验证授权、长度和互斥；合法后跳过选择数量校验。无自由输入时调用现有 `ValidateSubmittedValue`。

- **Step 5：实现结果快照与 Broker Submit**

`UserDecisionResult` 增加：

```csharp
private readonly ReadOnlyDictionary<string, string> customInputs;
public IReadOnlyDictionary<string, string> CustomInputs => customInputs;
```

`Submit` 接收可选映射并复制。Broker 的接口与实现使用：

```csharp
bool Submit(
    string requestId,
    string claimantId,
    IReadOnlyDictionary<string, object?> values,
    IReadOnlyDictionary<string, string>? customInputs = null);
```

Broker 把 null 转为空字典，分别创建快照，再调用新校验重载。成功日志只增加 `{CustomInputCount}` 数量字段。

- **Step 6：运行定向测试确认绿灯**

运行 Step 3 命令，预期全部通过且现有并发/Claim 测试无回归。

- **Step 7：提交 Task 2**

```powershell
git add LoomX/Assistant/UserDecisions/UserDecisionModels.cs LoomX/Assistant/UserDecisions/UserDecisionBroker.cs LoomX.Tests/Assistant/UserDecisionBrokerTestExtensions.cs LoomX.Tests/Assistant/UserDecisionModelsTests.cs LoomX.Tests/Assistant/UserDecisionBrokerTests.cs
git commit -m "支持 AskUser 自由输入结果快照"
```

### Task 3：修复工具结果中的实际文本回传

**文件：**
- 修改：`LoomX/Assistant/AssistantTools.cs`
- 测试：`LoomX.Tests/Assistant/AssistantToolsTests.cs`
- 测试：`LoomX.Tests/Assistant/AssistantServiceTests.cs`

**接口：**
- 产生：工具结果根对象固定包含 `custom_inputs`
- 修改：text 字段在 `values` 中直接序列化实际字符串
- 保持：预设单选和多选仍返回原始 option id 类型

- **Step 1：更新工具结果测试为实际字符串**

把现有 `provided` 断言改为：

```csharp
Assert.Equal("用户实际说明", result["values"]!["note"]!.GetValue<string>());
Assert.Empty(result["custom_inputs"]!.AsObject());
```

新增选择自由输入结果测试：Broker 使用 `values["mode"] = null` 和 `customInputs["mode"] = "自定义模式"` 提交后断言：

```csharp
Assert.Null(result["values"]!["mode"]);
Assert.Equal("自定义模式", result["custom_inputs"]!["mode"]!.GetValue<string>());
```

在 `AssistantServiceTests` 验证第二轮模型请求收到的 tool result 含实际文本与 `custom_inputs`。

- **Step 2：运行测试确认红灯**

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests" --no-restore
```

预期：text 仍为 `provided` 对象，根对象缺少 `custom_inputs`。

- **Step 3：修改 SerializeResult**

删除 text 特判，统一序列化 `result.Values`：

```csharp
values[pair.Key] = JsonSerializer.SerializeToNode(pair.Value, OutputJsonOptions);
```

构造自由输入对象：

```csharp
var customInputs = new JsonObject();
foreach (var pair in result.CustomInputs)
{
    customInputs[pair.Key] = pair.Value;
}
```

最终结果固定包含 `custom_inputs`。不得把该对象写入日志。

- **Step 4：运行定向测试确认绿灯**

运行 Step 2 命令，预期全部通过。

- **Step 5：提交 Task 3**

```powershell
git add LoomX/Assistant/AssistantTools.cs LoomX.Tests/Assistant/AssistantToolsTests.cs LoomX.Tests/Assistant/AssistantServiceTests.cs
git commit -m "修复 AskUser 实际文本回传"
```

### Task 4：实现选择字段 ViewModel 互斥状态与提交投影

**文件：**
- 修改：`LoomX/ViewModels/AskUserDialogViewModel.cs`
- 修改：`LoomX/ViewModels/AssistantViewModel.cs`
- 测试：`LoomX.Tests/Views/AskUserDialogContractTests.cs`
- 测试：`LoomX.Tests/Assistant/AssistantViewModelTests.cs`

**接口：**
- 产生：选择字段 `AllowsCustomInput`、`CustomInputPlaceholder`、`CustomInputMaxLength`、`CustomInput`
- 产生：`TryBuildResult(out values, out customInputs)`
- 保持：旧 `TryBuildResult(out values)` 转发新重载，减少测试迁移

- **Step 1：为单选互斥与默认提示增加失败测试**

创建启用自由输入且默认选中 `safe` 的单选字段，断言：

```csharp
var field = Assert.IsType<AskUserSingleSelectFieldViewModel>(viewModel.CurrentField);
Assert.True(field.AllowsCustomInput);
Assert.Equal("我有其他想法...", field.CustomInputPlaceholder);

field.CustomInput = "我想逐步确认";
Assert.Null(field.SelectedOptionId);

field.Options.Single(option => option.Id == "safe").IsSelected = true;
Assert.Equal(string.Empty, field.CustomInput);
```

再验证自定义 placeholder 和 max length。

- **Step 2：为多选互斥、跳过和结果投影增加失败测试**

覆盖：输入文字清除所有 CheckBox；重新选中任一项清空文字；`ClearValue` 同时清空两者；多页 Previous/Next 后文字保留；最终：

```csharp
Assert.True(viewModel.TryBuildResult(out var values, out var customInputs));
Assert.Empty(Assert.IsAssignableFrom<IEnumerable<string>>(values["features"]));
Assert.Equal("只启用本地索引", customInputs["features"]);
```

- **Step 3：运行测试确认红灯**

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AskUserDialogContractTests|FullyQualifiedName~AssistantViewModelTests" --no-restore
```

预期：选择字段无自由输入属性，结果只有一个映射。

- **Step 4：实现字段基类与双映射构造**

在基类增加：

```csharp
internal string? GetCustomInput() => isSkipped ? null : GetCustomInputCore();
protected virtual string? GetCustomInputCore() => null;
```

`AskUserDialogViewModel` 增加 `BuildCustomInputs()`，并提供：

```csharp
public bool TryBuildResult(
    out IReadOnlyDictionary<string, object?> values,
    out IReadOnlyDictionary<string, string> customInputs)
```

所有当前字段和最终校验都同时传入两个映射。

- **Step 5：实现单选和多选互斥状态**

两个选择字段共享相同的公开属性命名，但保留各自选择逻辑。使用 `updatingSelection` 防止递归：

```csharp
if (!string.IsNullOrWhiteSpace(customInput))
{
    ClearSelectionsWithoutNotification();
}
```

option 选中时执行 `SetCustomInputWithoutSelectionReset(string.Empty)`。批量清理完成后只调用一次 `NotifyValueChanged()`。

- **Step 6：让 AssistantViewModel 提交两个映射**

修改提交路径：

```csharp
if (!dialogViewModel.TryBuildResult(out var values, out var customInputs))
{
    // 保持现有失败处理
}

var submitSucceeded = SubmitOwnedUserDecision(
    broker,
    pending.RequestId,
    values,
    customInputs);
```

`SubmitOwnedUserDecision` 将两个映射传给 Broker。测试替身新增 `SubmittedCustomInputs`，断言实际原文到达 Broker。

- **Step 7：运行定向测试确认绿灯**

运行 Step 3 命令，预期全部通过。

- **Step 8：提交 Task 4**

```powershell
git add LoomX/ViewModels/AskUserDialogViewModel.cs LoomX/ViewModels/AssistantViewModel.cs LoomX.Tests/Views/AskUserDialogContractTests.cs LoomX.Tests/Assistant/AssistantViewModelTests.cs
git commit -m "实现 AskUser 选择题自由输入交互"
```

### Task 5：接入 Avalonia 输入框与本地化 Watermark

**文件：**
- 修改：`LoomX/Views/AskUserCard.axaml`
- 修改：`LoomX/Views/AskUserCard.axaml.cs`
- 修改：`LoomX/Resources/Strings.resx`
- 修改：`LoomX/Resources/Strings.en-US.resx`
- 修改：`LoomX/Resources/Strings.ja-JP.resx`
- 修改：`LoomX/Resources/Strings.zh-TW.resx`
- 测试：`LoomX.Tests/Views/AskUserDialogContractTests.cs`
- 测试：`LoomX.Tests/Views/AssistantViewStyleTests.cs`

**接口：**
- 产生：资源键 `assistant.decision.custom_input_placeholder`
- 产生：`SelectionCustomInput_OnKeyDown`
- 保持：RadioButton 点击仍是唯一单选自动前进入口

- **Step 1：增加 XAML 与 Locale 失败契约测试**

源码契约至少断言：

```csharp
Assert.Contains("Text=\"{Binding CustomInput, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
Assert.Contains("Watermark=\"{Binding CustomInputPlaceholder}\"", xaml, StringComparison.Ordinal);
Assert.Contains("IsVisible=\"{Binding AllowsCustomInput}\"", xaml, StringComparison.Ordinal);
Assert.Contains("SelectionCustomInput_OnKeyDown", xaml, StringComparison.Ordinal);
Assert.DoesNotContain("其他（可选）", xaml, StringComparison.Ordinal);
```

Locale 测试要求四个 resx 都包含新 key，简中 Value 精确为 `我有其他想法...`。

- **Step 2：运行视图测试确认红灯**

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AskUserDialogContractTests|FullyQualifiedName~AssistantViewStyleTests" --no-restore
```

预期：XAML 和资源缺少自由输入定义。

- **Step 3：修改单选和多选模板**

在两个 `ItemsControl` 后分别加入同构 TextBox：

```xml
<TextBox Text="{Binding CustomInput, Mode=TwoWay}"
         Watermark="{Binding CustomInputPlaceholder}"
         MaxLength="{Binding CustomInputMaxLength}"
         IsVisible="{Binding AllowsCustomInput}"
         KeyDown="SelectionCustomInput_OnKeyDown"/>
```

不添加额外标签；错误 TextBlock 继续放在输入框之后，确保自由输入校验错误可见。

- **Step 4：实现 Enter 行为和本地化资源**

代码后置：

```csharp
private void SelectionCustomInput_OnKeyDown(object? sender, KeyEventArgs e)
{
    if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
    {
        AdvanceOrSubmit();
        e.Handled = true;
    }
}
```

资源值：

- zh-CN：`我有其他想法...`
- en-US：`I have another idea...`
- ja-JP：`ほかの考えがあります...`
- zh-TW：`我有其他想法...`

- **Step 5：运行视图和相关 ViewModel 测试确认绿灯**

运行 Step 2 命令，再运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AssistantViewModelTests" --no-restore
```

预期全部通过。

- **Step 6：提交 Task 5**

```powershell
git add LoomX/Views/AskUserCard.axaml LoomX/Views/AskUserCard.axaml.cs LoomX/Resources/Strings.resx LoomX/Resources/Strings.en-US.resx LoomX/Resources/Strings.ja-JP.resx LoomX/Resources/Strings.zh-TW.resx LoomX.Tests/Views/AskUserDialogContractTests.cs LoomX.Tests/Views/AssistantViewStyleTests.cs
git commit -m "优化 AskUser 自由输入卡片体验"
```

### Task 6：日志安全、完整验证与发布

**文件：**
- 修改：`LoomX.Tests/Assistant/UserDecisionBrokerTests.cs`
- 修改：`LoomX.Tests/Assistant/AssistantViewModelTests.cs`
- 修改：`openspec/changes/enhance-ask-user-custom-input/tasks.md`
- 创建：`outputs/<运行时生成目录>/`

**接口：**
- 验证：任何 logger 捕获内容不包含普通文本字段或选择题自由输入原文
- 验证：最终发布包包含本 change 的可执行桌面端

- **Step 1：增加日志泄漏回归测试**

使用现有 `RecordingLogger<T>` 或测试内 RecordingLogger，提交两个标识文本：

```csharp
const string textSecret = "TEXT_SECRET_7F31";
const string customSecret = "CUSTOM_SECRET_8A42";
```

完成请求后把所有日志文本拼接，并断言：

```csharp
Assert.DoesNotContain(textSecret, logs, StringComparison.Ordinal);
Assert.DoesNotContain(customSecret, logs, StringComparison.Ordinal);
```

- **Step 2：运行全部 AskUser 定向测试**

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~UserDecision|FullyQualifiedName~AskUser|FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AssistantViewModelTests" --no-restore
```

预期：0 failed。

- **Step 3：运行 OpenSpec 严格校验**

```powershell
openspec validate enhance-ask-user-custom-input --strict
```

预期：`Change 'enhance-ask-user-custom-input' is valid`。

- **Step 4：运行完整测试与 Release 构建**

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore
dotnet build Loom-X.sln -c Release --no-restore
```

如果解决方案文件名不同，先用 `Get-ChildItem -Filter *.sln*` 读取实际名称，再对该文件执行同一命令。预期：所有测试通过，构建 0 error。

- **Step 5：同步 OpenSpec 任务完成状态**

逐项核对 `openspec/changes/enhance-ask-user-custom-input/tasks.md`，仅把已有验证证据的任务改为 `[x]`。运行：

```powershell
openspec status --change enhance-ask-user-custom-input --json
```

预期：实现任务全部完成。

- **Step 6：发布时间戳桌面包**

```powershell
$stamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
$outputDir = Join-Path 'outputs' "$stamp-ask-user-custom-input"
dotnet publish LoomX/LoomX.csproj -c Release -r win-x64 --self-contained false -o $outputDir
```

确认 `$outputDir` 下存在 `LoomX.exe`。不得删除或覆盖其他 outputs 目录。

- **Step 7：使用 cua-driver 完成桌面验收**

按项目规则隐藏启动新发布的 `LoomX.exe`，用进程 `Path` 确认启动的是 `$outputDir` 版本。通过本地 AskUser 测试入口依次验证：

1. 单选字段出现 Watermark“我有其他想法...”，无“其他（可选）”标签。
2. 输入文字后预设 RadioButton 清空；重新选择 option 后文字清空。
3. 多选字段同样互斥，Previous/Next 后状态保留。
4. 必填字段可用自由输入提交；空白文本不能提交。
5. 普通 text 字段和 `custom_inputs` 原文在后续 AI 工具结果中可见。

截图只截 LoomX 应用窗口，不截全屏；透明主题下不依据截图颜色武断判断主题配色。

- **Step 8：提交验证与流程产物**

```powershell
git add openspec/changes/enhance-ask-user-custom-input docs/superpowers/specs/2026-09-20-ask-user-custom-input-design.md docs/superpowers/plans/2026-09-20-ask-user-custom-input.md LoomX.Tests
git commit -m "完成 AskUser 自由输入验证与交付"
```

不要提交 `outputs/`，除非仓库现有规则明确要求跟踪发布包。

## 实施与审查记录

- TDD：各任务均先运行失败测试确认能力缺失，再完成最小实现并转绿。
- 本地标准审查：检查 `base-ref..HEAD` 全量差异、选择与自由输入互斥状态机、双映射兼容性及日志安全边界，未发现 CRITICAL 或 IMPORTANT 问题。因本 change 明确禁用后台子代理，审查在主会话完成。
- 自动验证：AskUser 定向测试 187 项通过；Release 全量测试 1110 项通过；OpenSpec strict 校验通过；Release 构建 0 error。
- 桌面验收：已验证默认 Watermark“我有其他想法...”、无“其他（可选）”标签、单选/多选互斥、重新选择清空自由文本、分页状态保留和必填自由输入提交。提交后外部模型返回非标准空响应，最终 AI 回复未生成；实际文本与 `custom_inputs` 进入后续模型请求由 `AssistantServiceTests` 自动化验证覆盖。
- 发布目录：`outputs/2026-09-20-043915-ask-user-custom-input`。
