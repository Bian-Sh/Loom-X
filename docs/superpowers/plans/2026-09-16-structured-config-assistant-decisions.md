---
change: structured-config-assistant-decisions
design-doc: docs/superpowers/specs/2026-09-16-structured-config-assistant-decisions-design.md
base-ref: a9e755d2ff2e924c6b23a589a027d8f8bca64a2d
---

# LoomX 结构化配置与 Assistant 用户决策实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 LoomX Assistant 增加安全的 TOML 结构化读写工具与可暂停/恢复的 AskUser 用户决策通道，为后续 Codex Client 配置集成提供通用底座。

**Architecture:** TOML 能力由独立文档服务负责语法树编辑和事务式文件写入，工具层只处理 JSON Schema、风险等级与脱敏；AskUser 由 singleton Broker 管理 pending request，Assistant 工具等待结构化结果，桌面 ViewModel/Dialog 负责展示、校验、提交与取消。现有 `ToolDefinition`、AgentLoop、数据库路径和 Browser Bridge 契约保持不变。

**Tech Stack:** .NET 10、C#、Avalonia 11.3.20、Tomlyn 2.10.1、Microsoft.Extensions.Logging、xUnit 2.9.3。

**Spec:** `docs/superpowers/specs/2026-09-16-structured-config-assistant-decisions-design.md`

## Global Constraints

- 所有文档、代码注释和 Git 提交消息使用中文；类型名、配置键和命令保持原文。
- TOML 路径必须使用 `string[]`，不得用 dotted string 作为唯一表示。
- 写入必须遵循 Read → Patch Candidate → Validate → Compare → Backup → Temp Write → Validate Temp → Atomic Replace → Validate Target。
- no-op 不创建备份、不重写文件。
- 业务日志必须使用注入的 `ILogger<T>` 和结构化模板；不得使用 `Console.WriteLine`、`Debug.WriteLine` 记录诊断。
- 工具结果、日志、Toast 和 AskUser 展示不得包含 API Key、Authorization、自定义 Header 值、完整请求/响应正文、完整 TOML 文档或用户自由文本结果。
- 不修改 `%LOCALAPPDATA%\LoomX\LoomX.db` 与 `%LOCALAPPDATA%\LoomX\LoomX.Activity.db` 的统一路径逻辑。
- 不实现 Codex Catalog、Codex `config.toml`、环境变量写入、Codex 重启、第三方搜索 Provider 或网站安全机制绕过。
- 每个任务遵循 Red → Green → Refactor；任务通过定向测试后才勾选对应 `openspec/changes/structured-config-assistant-decisions/tasks.md` 项并提交。

## 文件结构

- Create: `LoomX/Assistant/Configuration/TomlModels.cs` — TOML 路径、值、Patch、读取/校验/写入结果契约。
- Create: `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs` — 统一敏感键识别与递归脱敏。
- Create: `LoomX/Assistant/Configuration/ITomlDocumentService.cs` — TOML 服务公共接口。
- Create: `LoomX/Assistant/Configuration/TomlDocumentService.cs` — Tomlyn 解析、查询、语法树编辑和事务式写入。
- Create: `LoomX/Assistant/TomlTools.cs` — 六个 `toml.*` ToolDefinition。
- Create: `LoomX/Assistant/UserDecisions/UserDecisionModels.cs` — AskUser 字段、请求、结果与校验契约。
- Create: `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs` — pending request 生命周期与并发控制。
- Create: `LoomX/Assistant/AssistantTools.cs` — `assistant.ask_user` ToolDefinition。
- Create: `LoomX/ViewModels/AskUserDialogViewModel.cs` — 字段投影、输入状态和提交校验。
- Create: `LoomX/Views/AskUserDialog.axaml`、`LoomX/Views/AskUserDialog.axaml.cs` — 动态 AskUser 表单。
- Modify: `LoomX/LoomX.csproj`、`LoomX.Tests/LoomX.Tests.csproj` — 引入 Tomlyn 2.10.1。
- Modify: `LoomX/LoomXHost.cs:48-111` — 注册 TOML 服务、Broker 与工具。
- Modify: `LoomX/Assistant/AssistantService.cs:25-47, 95-205` — 暴露运行取消边界并取消 pending AskUser。
- Modify: `LoomX/ViewModels/AssistantViewModel.cs:18-70, 250-285, 560-600` — 订阅 Broker、显示 Dialog、页面卸载取消。
- Modify: `LoomX/Assistant/AssistantService.cs:15-23` 或 Skill 文档 — 补充资料通道和挑战交还用户的约束。
- Test: `LoomX.Tests/Assistant/TomlModelsTests.cs`
- Test: `LoomX.Tests/Assistant/SensitiveKeyPolicyTests.cs`
- Test: `LoomX.Tests/Assistant/TomlDocumentServiceTests.cs`
- Test: `LoomX.Tests/Assistant/TomlToolsTests.cs`
- Test: `LoomX.Tests/Assistant/UserDecisionModelsTests.cs`
- Test: `LoomX.Tests/Assistant/UserDecisionBrokerTests.cs`
- Test: `LoomX.Tests/Assistant/AssistantToolsTests.cs`
- Test: `LoomX.Tests/Assistant/AssistantServiceTests.cs`
- Test: `LoomX.Tests/Assistant/AssistantViewModelTests.cs`
- Test: `LoomX.Tests/Views/AskUserDialogContractTests.cs`

---

### Task 1: TOML 依赖、领域契约与敏感键策略

**Files:**
- Modify: `LoomX/LoomX.csproj`
- Modify: `LoomX.Tests/LoomX.Tests.csproj`
- Create: `LoomX/Assistant/Configuration/TomlModels.cs`
- Create: `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs`
- Create: `LoomX.Tests/Assistant/TomlModelsTests.cs`
- Create: `LoomX.Tests/Assistant/SensitiveKeyPolicyTests.cs`
- Modify: `openspec/changes/structured-config-assistant-decisions/tasks.md`

**Interfaces:**
- Produces: `TomlPath`, `TomlValueKind`, `TomlValue`, `TomlPatchOperation`, `TomlValidationResult`, `TomlValueResult`, `TomlWriteResult`。
- Produces: `SensitiveKeyPolicy.IsSensitivePath(IReadOnlyList<string>)` 与 `SensitiveKeyPolicy.Redact(TomlValue, IReadOnlyList<string>)`。

- [x] **Step 1: 添加 Tomlyn 2.10.1 包引用并恢复依赖**

在 `LoomX/LoomX.csproj` 与 `LoomX.Tests/LoomX.Tests.csproj` 的 PackageReference ItemGroup 分别加入：

```xml
<PackageReference Include="Tomlyn" Version="2.10.1" />
```

Run: `dotnet restore LoomX.slnx`
Expected: restore 成功，未修改数据库路径相关源码。

- [x] **Step 2: 编写 TOML 值与路径失败测试**

在 `TomlModelsTests` 覆盖：空 path、空 segment、string/int64/double/bool/array/object、超出 Int64 的 JSON integer、不支持 null 与任意对象扩展。核心断言示例：

```csharp
[Fact]
public void TomlPath_RejectsEmptySegment()
{
    var error = Assert.Throws<ArgumentException>(() => new TomlPath(["model_providers", ""]));
    Assert.Contains("路径", error.Message, StringComparison.Ordinal);
}

[Theory]
[InlineData("api_key")]
[InlineData("Authorization")]
[InlineData("refresh-token")]
public void SensitiveKeyPolicy_RecognizesSensitiveSegments(string segment)
{
    Assert.True(SensitiveKeyPolicy.IsSensitivePath(["provider", segment]));
}
```

- [x] **Step 3: 运行测试确认红灯**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~TomlModelsTests|FullyQualifiedName~SensitiveKeyPolicyTests"`
Expected: FAIL，类型尚不存在。

- [x] **Step 4: 实现最小领域契约和统一脱敏策略**

契约保持不可变，Patch 明确区分 set/delete：

```csharp
public readonly record struct TomlPath(IReadOnlyList<string> Segments);

public enum TomlPatchKind { Set, Delete }

public sealed record TomlPatchOperation(
    TomlPatchKind Kind,
    TomlPath Path,
    TomlValue? Value = null);

public sealed record TomlWriteResult(
    bool Success,
    bool Changed,
    string? BackupPath,
    bool FormattingChanged,
    IReadOnlyList<string> Errors);
```

`SensitiveKeyPolicy` 对 segment 做小写与 `-`/`_` 归一化，只返回固定 `***` 占位符，不保留原值长度。

- [x] **Step 5: 运行定向测试和格式检查**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~TomlModelsTests|FullyQualifiedName~SensitiveKeyPolicyTests"`
Expected: PASS。

Run: `dotnet format LoomX.slnx --verify-no-changes --no-restore`
Expected: PASS；若仅新增文件格式不符，先运行 `dotnet format LoomX.slnx --no-restore` 再复验。

- [x] **Step 6: 勾选 OpenSpec 1.1–1.3 并提交**

```powershell
git add LoomX/LoomX.csproj LoomX.Tests/LoomX.Tests.csproj LoomX/Assistant/Configuration LoomX.Tests/Assistant/TomlModelsTests.cs LoomX.Tests/Assistant/SensitiveKeyPolicyTests.cs openspec/changes/structured-config-assistant-decisions/tasks.md
git commit -m "新增 TOML 领域契约与敏感键策略"
```

### Task 2: TOML 读取、路径查询与语法校验

**Files:**
- Modify: `LoomX/Assistant/Configuration/TomlModels.cs` — 补充不可变 `TomlReadResult`（`Exists`、`IsValid`、`TopLevelKeys`、`Errors`）
- Create: `LoomX/Assistant/Configuration/ITomlDocumentService.cs`
- Create: `LoomX/Assistant/Configuration/TomlDocumentService.cs`
- Create: `LoomX.Tests/Assistant/TomlDocumentServiceTests.cs`
- Modify: `openspec/changes/structured-config-assistant-decisions/tasks.md`

**Interfaces:**
- Consumes: Task 1 的 `TomlPath`、`TomlValue`、`SensitiveKeyPolicy`。
- Produces:

```csharp
public interface ITomlDocumentService
{
    Task<TomlReadResult> ReadAsync(string path, CancellationToken cancellationToken = default);
    Task<TomlValueResult> GetAsync(string path, TomlPath keyPath, CancellationToken cancellationToken = default);
    Task<TomlValidationResult> ValidateAsync(string path, CancellationToken cancellationToken = default);
    Task<TomlWriteResult> PatchAsync(string path, IReadOnlyList<TomlPatchOperation> operations, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: 编写读取、查询与解析失败测试**

使用 xUnit 临时目录覆盖：nested table、quoted key、包含点号的 key、dotted key、数组、数组表、空文件、未闭合字符串、缺失文件、中文和空格路径。示例：

```csharp
[Fact]
public async Task GetAsync_DistinguishesQuotedDotKeyFromNestedPath()
{
    var file = WriteToml("[root]\n\"a.b\" = \"quoted\"\n[root.a]\nb = \"nested\"\n");
    var service = CreateService();

    var quoted = await service.GetAsync(file, new TomlPath(["root", "a.b"]));
    var nested = await service.GetAsync(file, new TomlPath(["root", "a", "b"]));

    Assert.Equal("quoted", quoted.Value?.Value);
    Assert.Equal("nested", nested.Value?.Value);
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~TomlDocumentServiceTests`
Expected: FAIL，服务尚不存在。

- [ ] **Step 3: 实现统一解析入口**

`TomlDocumentService` 构造函数注入 `ILogger<TomlDocumentService>`；内部 `ParseDocument` 同时服务于 Read/Get/Validate。限制文件大小，解析错误只返回行列和消息，不回显原文：

```csharp
public TomlDocumentService(ILogger<TomlDocumentService> logger)
{
    this.logger = logger;
}
```

查询通过语法节点真实 key segment 遍历，读取值后转换为 Task 1 的受控 `TomlValue`，敏感路径在返回前脱敏。

- [ ] **Step 4: 添加结构化日志捕获测试**

使用测试 Logger 验证成功日志包含操作类型、文件安全摘要和耗时；解析失败日志包含异常/错误类型，但不包含 TOML 原文和 `secret-value-123`。

- [ ] **Step 5: 运行定向测试**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~TomlDocumentServiceTests`
Expected: PASS。

- [ ] **Step 6: 勾选 OpenSpec 2.1 与读取/日志相关项并提交**

```powershell
git add LoomX/Assistant/Configuration/TomlModels.cs LoomX/Assistant/Configuration/ITomlDocumentService.cs LoomX/Assistant/Configuration/TomlDocumentService.cs LoomX.Tests/Assistant/TomlDocumentServiceTests.cs openspec/changes/structured-config-assistant-decisions/tasks.md
git commit -m "实现 TOML 读取查询与语法校验"
```

### Task 3: TOML Patch、备份、原子替换与失败回滚

**Files:**
- Modify: `LoomX/Assistant/Configuration/TomlDocumentService.cs`
- Create: `LoomX/Assistant/Configuration/TomlFileOperations.cs`
- Modify: `LoomX.Tests/Assistant/TomlDocumentServiceTests.cs`
- Modify: `openspec/changes/structured-config-assistant-decisions/tasks.md`

**Interfaces:**
- Consumes: `ITomlDocumentService.PatchAsync`。
- Produces: internal `ITomlFileOperations`，只抽象测试必须控制的 copy/write/replace/move/delete/delay 行为；默认实现直接调用 `File` API。

- [ ] **Step 1: 编写 Patch 原子性和格式保留失败测试**

覆盖 set、delete、父表创建、标量穿越拒绝、批量中途失败不落盘、注释/未知 section 保留、空父表保留、同值 no-op。示例：

```csharp
[Fact]
public async Task PatchAsync_WhenSecondOperationIsInvalid_LeavesOriginalUntouched()
{
    var original = "# keep\n[provider]\nname = \"demo\"\n";
    var file = WriteToml(original);

    var result = await CreateService().PatchAsync(file,
    [
        Set(["provider", "name"], "changed"),
        Set(["provider", "name", "child"], "invalid")
    ]);

    Assert.False(result.Success);
    Assert.Equal(original, await File.ReadAllTextAsync(file));
    Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.bak"));
}
```

- [ ] **Step 2: 运行 Patch 测试确认红灯**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~TomlDocumentServiceTests&Name~Patch"`
Expected: FAIL，Patch 尚未实现。

- [ ] **Step 3: 实现语法树候选编辑与重新解析**

全部操作先应用于内存候选；delete 只移除目标节点，不清理父表；set 创建缺失普通 table，但拒绝跨越标量或数组表。候选文本必须重新解析成功才进入文件事务。

- [ ] **Step 4: 编写文件事务失败测试**

通过 fake `ITomlFileOperations` 注入以下故障：临时文件写入失败、临时解析失败、Replace 抛 `IOException` 两次后成功、Replace 最终失败、写后目标解析失败且备份恢复成功、恢复失败。每条测试断言原文件或备份可恢复，并断言日志不含 TOML/Secret。

- [ ] **Step 5: 实现事务式写入和有限重试**

默认实现使用同目录路径：

```csharp
var backupPath = $"{path}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.bak";
var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
```

目标存在时先 Copy 到备份；写临时文件并解析；Windows 目标存在时 `File.Replace(tempPath, path, null)`，不存在时 `File.Move(tempPath, path)`。仅对 `IOException`/`UnauthorizedAccessException` 做固定次数短延迟重试，并响应 CancellationToken。

- [ ] **Step 6: 运行 TOML 服务全部测试**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~TomlDocumentServiceTests`
Expected: PASS，包含中文路径、空格路径、no-op、占用重试和恢复测试。

- [ ] **Step 7: 勾选 OpenSpec 2.2–2.5 并提交**

```powershell
git add LoomX/Assistant/Configuration/TomlDocumentService.cs LoomX/Assistant/Configuration/TomlFileOperations.cs LoomX.Tests/Assistant/TomlDocumentServiceTests.cs openspec/changes/structured-config-assistant-decisions/tasks.md
git commit -m "实现 TOML 原子补丁与失败回滚"
```

### Task 4: 注册六个 TOML Assistant 工具

**Files:**
- Create: `LoomX/Assistant/TomlTools.cs`
- Modify: `LoomX/LoomXHost.cs:48-111`
- Create: `LoomX.Tests/Assistant/TomlToolsTests.cs`
- Modify: `LoomX.Tests/Assistant/ToolRegistryTests.cs`
- Modify: `openspec/changes/structured-config-assistant-decisions/tasks.md`

**Interfaces:**
- Consumes: `ITomlDocumentService`。
- Produces: `TomlTools.RegisterAll(ToolRegistry registry, ITomlDocumentService service)`。

- [ ] **Step 1: 编写工具注册、Schema 和风险等级失败测试**

断言名称与风险：

```csharp
var expected = new Dictionary<string, ToolRiskLevel>
{
    ["toml.read"] = ToolRiskLevel.Read,
    ["toml.get"] = ToolRiskLevel.Read,
    ["toml.validate"] = ToolRiskLevel.Read,
    ["toml.set"] = ToolRiskLevel.Write,
    ["toml.patch"] = ToolRiskLevel.Write,
    ["toml.delete"] = ToolRiskLevel.Destructive,
};
```

Schema 必须要求 `path` 文件路径；get/set/delete 要求 `key_path` 字符串数组；patch 要求非空 `operations`，每项只能为 set/delete。

- [ ] **Step 2: 运行测试确认红灯**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~TomlToolsTests|FullyQualifiedName~ToolRegistryTests"`
Expected: FAIL，工具尚未注册。

- [ ] **Step 3: 实现 ToolDefinition 和安全 JSON 结果**

`TomlTools` 只解析参数、限制数组长度/字符串长度、调用服务并序列化安全结果；不得返回完整 TOML 文本或 set 值。错误使用固定 error code 与安全消息。

- [ ] **Step 4: 接入 LoomXHost DI 和 ToolRegistry**

在 `LoomXHost` 注册 singleton `ITomlDocumentService`，随后在现有 registry factory 内调用：

```csharp
Assistant.TomlTools.RegisterAll(
    registry,
    services.GetRequiredService<Assistant.Configuration.ITomlDocumentService>());
```

不得改变 `AppDataPaths` 或 SQLite 注册。

- [ ] **Step 5: 添加权限、取消和敏感输出测试**

验证 Write/Destructive 在逐条审批模式下触发既有 AgentLoop 审批；取消令牌中断服务调用；`api_key = "plain-secret"` 不出现在 ToolResult 或捕获日志。

- [ ] **Step 6: 运行定向测试并提交**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~TomlToolsTests|FullyQualifiedName~ToolRegistryTests|FullyQualifiedName~AgentLoopTests"`
Expected: PASS。

勾选 OpenSpec 3.1–3.3 后提交：

```powershell
git add LoomX/Assistant/TomlTools.cs LoomX/LoomXHost.cs LoomX.Tests/Assistant/TomlToolsTests.cs LoomX.Tests/Assistant/ToolRegistryTests.cs openspec/changes/structured-config-assistant-decisions/tasks.md
git commit -m "接入 TOML Assistant 工具"
```

### Task 5: AskUser 领域模型与 UserDecisionBroker

**Files:**
- Create: `LoomX/Assistant/UserDecisions/UserDecisionModels.cs`
- Create: `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs`
- Create: `LoomX.Tests/Assistant/UserDecisionModelsTests.cs`
- Create: `LoomX.Tests/Assistant/UserDecisionBrokerTests.cs`
- Modify: `openspec/changes/structured-config-assistant-decisions/tasks.md`

**Interfaces:**
- Produces: `UserDecisionRequest`, `UserDecisionField` 四种判别类型、`UserDecisionOption`、`UserDecisionResult`。
- Produces: `UserDecisionBroker.RequestAsync`、`Submit`、`Cancel`、`CancelOwner`、`PendingRequested`。

```csharp
public sealed record PendingUserDecision(string RequestId, string OwnerId, UserDecisionRequest Request);

public interface IUserDecisionBroker
{
    event EventHandler<PendingUserDecision>? PendingRequested;
    Task<UserDecisionResult> RequestAsync(string ownerId, UserDecisionRequest request, CancellationToken cancellationToken);
    bool Submit(string requestId, IReadOnlyDictionary<string, object?> values);
    bool Cancel(string requestId, string reason);
    int CancelOwner(string ownerId, string reason);
}
```

- [ ] **Step 1: 编写模型校验失败测试**

覆盖重复 field id、空标题、未知默认选项、multi min/max、number 范围/步长、必填空文本、过长文本、敏感模式内容。取消结果不得携带默认字段值。

- [ ] **Step 2: 运行模型测试确认红灯**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~UserDecisionModelsTests`
Expected: FAIL。

- [ ] **Step 3: 实现不可变模型与集中校验器**

字段用 enum + 明确属性建模，不使用任意 JsonObject 贯穿 UI。校验错误返回字段 id 与中文安全消息；问题、选项说明和影响摘要复用 `SensitiveKeyPolicy` 与长度上限。

- [ ] **Step 4: 编写 Broker 并发和生命周期失败测试**

覆盖并发 request id 唯一、事件只发布一次、Submit/Cancel、CancellationToken、CancelOwner、重复完成 false、事件订阅者抛异常不遗留 pending、continuation 异步执行。

- [ ] **Step 5: 实现 Broker**

使用 `ConcurrentDictionary<string, PendingEntry>` 与：

```csharp
new TaskCompletionSource<UserDecisionResult>(TaskCreationOptions.RunContinuationsAsynchronously)
```

所有完成路径先 `TryRemove`，再 `TrySetResult`/`TrySetCanceled`；CancellationTokenRegistration 在完成后释放。

- [ ] **Step 6: 运行定向测试并提交**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~UserDecisionModelsTests|FullyQualifiedName~UserDecisionBrokerTests"`
Expected: PASS。

勾选 OpenSpec 4.1–4.2、4.4 的模型/Broker部分后提交：

```powershell
git add LoomX/Assistant/UserDecisions LoomX.Tests/Assistant/UserDecisionModelsTests.cs LoomX.Tests/Assistant/UserDecisionBrokerTests.cs openspec/changes/structured-config-assistant-decisions/tasks.md
git commit -m "新增 AskUser 决策模型与 Broker"
```

### Task 6: `assistant.ask_user` 工具与 Assistant 会话恢复

**Files:**
- Create: `LoomX/Assistant/AssistantTools.cs`
- Modify: `LoomX/LoomXHost.cs:72-110`
- Modify: `LoomX/Assistant/AssistantService.cs:25-47, 95-205, 220-260`
- Create: `LoomX.Tests/Assistant/AssistantToolsTests.cs`
- Modify: `LoomX.Tests/Assistant/AssistantServiceTests.cs`
- Modify: `openspec/changes/structured-config-assistant-decisions/tasks.md`

**Interfaces:**
- Consumes: `IUserDecisionBroker`。
- Produces: `AssistantTools.RegisterAll(ToolRegistry registry, IUserDecisionBroker broker)`，注册 `assistant.ask_user`，风险等级为 Read。

- [ ] **Step 1: 编写工具等待、提交和取消失败测试**

构造 ToolDefinition Handler 后启动未完成任务，捕获 Broker pending request，再 Submit；断言工具任务恢复并返回字段 id 映射。Cancel 返回：

```json
{"cancelled":true,"values":{}}
```

敏感请求在进入 pending 前失败，ToolResult 不含原始敏感文本。

- [ ] **Step 2: 运行测试确认红灯**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~AssistantToolsTests`
Expected: FAIL。

- [ ] **Step 3: 实现 `assistant.ask_user` 并注册 DI**

JSON Schema 支持 title、question、reason、impact_summary、allow_cancel、fields；Handler 把 JSON 转为强类型模型，通过当前 tool CancellationToken 调用 Broker，不自行阻塞线程。

- [ ] **Step 4: 编写 AssistantService 集成失败测试**

用 scripted model 先返回 `assistant.ask_user` tool call，再在提交后返回最终文本；断言 Session 消息、ToolResult 和后续回答连续。另测 `Stop()`、新会话和 CancellationToken 会取消 pending request。

- [ ] **Step 5: 最小修改 AssistantService 生命周期**

为一次 Run 生成稳定 owner id，并在停止/切换会话的 finally 路径调用：

```csharp
userDecisionBroker.CancelOwner(ownerId, "assistant_run_cancelled");
```

不修改现有 `ApprovalHandler` 和 `ToolApprovalGate` 语义。

- [ ] **Step 6: 运行定向测试并提交**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AgentLoopTests"`
Expected: PASS。

勾选 OpenSpec 4.3 与 4.4 剩余部分后提交：

```powershell
git add LoomX/Assistant/AssistantTools.cs LoomX/Assistant/AssistantService.cs LoomX/LoomXHost.cs LoomX.Tests/Assistant/AssistantToolsTests.cs LoomX.Tests/Assistant/AssistantServiceTests.cs openspec/changes/structured-config-assistant-decisions/tasks.md
git commit -m "接入 AskUser 工具与会话恢复"
```

### Task 7: AskUser Dialog、ViewModel 与 Toast

**Files:**
- Create: `LoomX/ViewModels/AskUserDialogViewModel.cs`
- Create: `LoomX/Views/AskUserDialog.axaml`
- Create: `LoomX/Views/AskUserDialog.axaml.cs`
- Modify: `LoomX/ViewModels/AssistantViewModel.cs:18-70, 250-285, 560-600`
- Modify: `LoomX.Tests/Assistant/AssistantViewModelTests.cs`
- Create: `LoomX.Tests/Views/AskUserDialogContractTests.cs`
- Modify: `openspec/changes/structured-config-assistant-decisions/tasks.md`

**Interfaces:**
- Consumes: `PendingUserDecision` 与 `IUserDecisionBroker`。
- Produces: `AskUserDialogViewModel.TryBuildResult(out IReadOnlyDictionary<string, object?> values)`。

- [ ] **Step 1: 编写 ViewModel 字段投影和校验失败测试**

覆盖 single/multi/number/text、默认值、必填、多选最少/最多、数字范围、文本长度、取消。示例：

```csharp
[Fact]
public void TryBuildResult_WhenRequiredTextIsEmpty_ReturnsFieldError()
{
    var viewModel = CreateTextDialog(required: true, value: "");
    Assert.False(viewModel.TryBuildResult(out _));
    Assert.Contains(viewModel.Fields, field => field.ErrorMessage?.Length > 0);
}
```

- [ ] **Step 2: 运行 ViewModel 测试确认红灯**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AskUserDialogContractTests"`
Expected: FAIL。

- [ ] **Step 3: 实现专用 Dialog ViewModel 和 XAML**

使用现有动态资源、圆角、边框与透明背景风格；通过字段类型选择 DataTemplate。Dialog 至少包含标题、问题、原因/影响摘要、滚动字段区、错误区、取消与提交按钮。不得把用户输入绑定到日志或 Toast。

- [ ] **Step 4: 接入 AssistantViewModel 生命周期**

激活时订阅 `PendingRequested`，UI 线程中打开 `AskUserDialog`；关闭页面/Dispose 时解除订阅并 `CancelOwner`。Dialog 关闭等价于取消；提交只调用 Broker 的 request id，不直接操纵 AgentLoop。

- [ ] **Step 5: 接入安全 Toast**

提交成功显示“已提交助手决策”，取消显示“已取消助手决策”，错误显示安全错误摘要；任何 Toast 不包含字段值、问题正文、API Key 或 Authorization。

- [ ] **Step 6: 运行 ViewModel、视图契约与外观测试**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AskUserDialogContractTests|FullyQualifiedName~WindowAppearanceCoordinatorTests"`
Expected: PASS。

- [ ] **Step 7: 勾选 OpenSpec 5.1–5.3 并提交**

```powershell
git add LoomX/ViewModels/AskUserDialogViewModel.cs LoomX/Views/AskUserDialog.axaml LoomX/Views/AskUserDialog.axaml.cs LoomX/ViewModels/AssistantViewModel.cs LoomX.Tests/Assistant/AssistantViewModelTests.cs LoomX.Tests/Views/AskUserDialogContractTests.cs openspec/changes/structured-config-assistant-decisions/tasks.md
git commit -m "实现 AskUser 桌面交互"
```

### Task 8: 资料通道约束、安全回归与完整交付

**Files:**
- Modify: `LoomX/Assistant/AssistantService.cs:15-23`
- Modify: `LoomX/Skills/` 下与 Client/Browser 使用说明最接近的现有 Skill；若没有合适 Skill，则创建 `LoomX/Skills/client-research/SKILL.md` 与 `manifest.json`
- Modify: `LoomX.Tests/Assistant/SkillStoreTests.cs`
- Modify: `openspec/changes/structured-config-assistant-decisions/tasks.md`
- Create: `outputs/<2026-09-16-HHmm>-structured-config-assistant-decisions/` 发布产物

**Interfaces:**
- Consumes: 已实现 `assistant.ask_user` 和现有 `browser.open/read/wait`。
- Produces: 明确的资料获取顺序与网站挑战处理说明，不新增搜索工具或 Secret。

- [ ] **Step 1: 编写文档/Skill 契约失败测试**

断言系统提示或 Skill 包含：优先模型已有能力、其次 Browser Bridge、最后 AskUser；并包含登录/CAPTCHA/Cloudflare/JS challenge 交还用户、不得绕过；断言不出现新的搜索 API Key 配置键。

- [ ] **Step 2: 运行 Skill 测试确认红灯**

Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~SkillStoreTests`
Expected: FAIL，约束尚未写入。

- [ ] **Step 3: 更新系统提示与 Skill 说明**

保持短而明确：模型有官方资料能力时直接使用；否则通过 Browser Bridge 读取用户授权页面；出现挑战时暂停；没有资料通道时用 AskUser 请求用户提供结论。不得增加 Browser Bridge 绕过代码。

- [ ] **Step 4: 运行新增能力完整定向测试**

Run:

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~Toml|FullyQualifiedName~UserDecision|FullyQualifiedName~AssistantTools|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AskUserDialogContractTests|FullyQualifiedName~SkillStoreTests"
```

Expected: PASS。

- [ ] **Step 5: 运行完整构建、测试与格式验证**

Run:

```powershell
dotnet build LoomX.slnx --no-restore
dotnet test LoomX.slnx --no-build
dotnet format LoomX.slnx --verify-no-changes --no-restore
```

Expected: 全部 PASS；日志测试确认无 Secret，数据库路径测试保持唯一位置。

- [ ] **Step 6: 运行 OpenSpec 验证并完成任务勾选**

Run:

```powershell
openspec status --change structured-config-assistant-decisions --json
openspec validate structured-config-assistant-decisions --strict
```

Expected: strict validate PASS，`tasks.md` 1.1–7.4 全部勾选。

- [ ] **Step 7: 发布 standalone 应用**

用当前时间生成可读目录名，例如 `outputs/2026-09-16-2230-structured-config-assistant-decisions/`，执行：

```powershell
dotnet publish LoomX/LoomX.csproj -c Release -r win-x64 --self-contained true -o outputs/2026-09-16-2230-structured-config-assistant-decisions
```

Expected: 发布目录包含可启动的 LoomX exe；使用 `Start-Process -FilePath <绝对exe路径>` 隐藏/正常启动并按进程 `Path` 校验，禁止通过 app resolver 按 exe 路径启动。

- [ ] **Step 8: 最终提交**

```powershell
git add LoomX LoomX.Tests openspec/changes/structured-config-assistant-decisions/tasks.md docs/superpowers/specs/2026-09-16-structured-config-assistant-decisions-design.md docs/superpowers/plans/2026-09-16-structured-config-assistant-decisions.md
git commit -m "完成结构化配置与助手决策能力"
```

发布产物是否纳入 Git 以仓库现有 `outputs/` 规则为准，不得删除其他 Session 的产物。

## 计划自检

- Spec coverage：TOML 读取/查询/校验、Patch、备份、原子写入、失败回滚、工具风险、脱敏、AskUser 模型/Broker/工具/UI、资料通道、完整验证和发布均有对应任务。
- Type consistency：`ITomlDocumentService`、`IUserDecisionBroker`、`PendingUserDecision`、`TomlPatchOperation` 在首次出现处定义，后续任务使用同一命名。
- Scope：不包含 Codex Catalog/Profile/configure、环境变量或重启逻辑；后续 Change 通过这里的公共接口接入。
- No placeholders：计划不含未决实现项；Skill 文件选择存在条件分支，但明确规定优先修改现有最接近 Skill，仅在不存在时创建固定路径。




