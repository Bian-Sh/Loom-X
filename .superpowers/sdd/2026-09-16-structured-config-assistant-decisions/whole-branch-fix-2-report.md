# structured-config-assistant-decisions 整分支修复 wave 2 报告

- 日期：2026-09-17
- 分支：`codex/structured-config-assistant-decisions`
- 起始 HEAD：`9e482dfca39fd8761be0682672362a8d30d57583`
- 范围：仅关闭 `whole-branch-fix-1-review.md` 残留 Critical C1，不处理已关闭的 C2/I1/I2/I3/I4/M1
- 约束：未修改数据库路径、计划/OpenSpec/Comet 状态、既有审查报告；未 publish、未 commit、未 push

## 根因复现与数据流定位

按 `systematic-debugging` 先追踪原始值从模型工具调用到所有安全出口的传播链：

1. `ToolArgumentSafety` 只保护 `ToolCall.ArgumentsJson`，没有保护 handler 的返回内容。
2. `ToolResult.Fail(string)` 与固定安全失败没有可信度差异；`AgentLoop` 将任意失败 `Content` 原样写入 Tool 消息、`MessageCompleted`、`ToolCallCompleted.Detail`、UI、jsonl 与下一轮 `ModelRequest`。
3. handler 未捕获异常时，`AgentLoop` 把原异常对象交给 `ILogger`，并把 `exception.Message` 拼入 `ToolResult`；异常 message、inner exception、`Exception.Data` 都可能含原始参数。
4. 未注册工具虽然隐藏了 arguments，但安全投影仍保留模型提供的原始工具名；同一原始名称继续进入 Session、事件、UI、jsonl、下一轮请求和日志。
5. `LoomXTools.FindProviderAsync`、Combo/Endpoint 查找把原始 id/name/key 拼入异常，`Guard/GuardAsync` 再用 `ToolResult.Fail(exception.Message)` 原样返回。

复现证据：

- 新增 AgentLoop 回归后首次运行：23 个 AgentLoop 测试中 4 个失败。普通 `ToolResult.Fail("private-header-value")`、handler 异常 message/完整路径、原始未注册工具名以及旧的“模拟工具故障”均被断言捕获。
- 新增真实 `loomx.get_provider` handler 回归后首次运行：1/1 失败，返回内容为 `Provider 'private-header-value' 不存在。`。

## 设计

### 1. 中央失败结果安全边界

- `ToolResult` 改为私有构造的 sealed record，保留值对象语义。
- `ToolResult.Fail` 永远创建“不可信失败”；普通调用无法设置内部安全标记。
- 新增显式 `ToolResult.SafeFail`，仅供实现方返回固定错误码/固定消息。
- `AgentLoop` 在任何失败写入 Session/Event/UI/model 前统一调用 `EnsureSafeFailure()`；不可信失败固定替换为 `工具执行失败。`，不根据内容正则猜测。
- 成功结果保持原行为，不扩大本 micro-fix 范围。

### 2. 固定安全失败语义

- AgentLoop 自身的未注册工具、无效 JSON、超时、未捕获 handler 异常改用固定 `SafeFail`。
- `TomlTools` 与 `AssistantTools` 的固定 JSON 错误 helper 改用 `SafeFail`，因此 `toml_operation_failed`、`invalid_request` 等可恢复错误码不会被中央边界泛化。
- `LoomXTools` 的固定失败改用 `SafeFail`；Guard 按异常类型映射固定类别，不再信任 `exception.Message`。
- Provider/Combo/Endpoint 查找异常不再拼接 id/name/key。

### 3. 异常日志边界

- handler 未捕获异常不再把原异常对象、message、inner exception 或 `Exception.Data` 交给 logger。
- logger 的异常参数改为新建的固定安全 `InvalidOperationException("工具处理器执行失败。")`。
- 仅通过结构化字段记录 handler 的异常类型全名；注册工具名来自代码定义，可继续记录。
- 返回给模型和 UI 的结果固定为 `工具执行失败。`。

### 4. 未注册工具名投影

- `ToolArgumentSafety.Project` 在查找失败时把 name 固定为 `unknown.tool`，arguments 固定隐藏。
- 原始 name 只用于当前 `ToolRegistry.TryGet` 查找；Session、事件、Tool 消息、UI、jsonl、后续模型请求和日志均不保留。
- `tool_call_id` 原样保留，保证 assistant tool call 与 Tool message 的协议配对。
- 已注册工具也统一使用 `ToolDefinition.Name` 作为安全规范名称。

## TDD：RED

### AgentLoop 中央边界

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~AgentLoopTests" -- RunConfiguration.MaxCpuCount=1
```

首次结果：失败 4、通过 19、总计 23。失败点分别是：

- 普通 `ToolResult.Fail` 原文进入 Session/Event/UI/jsonl/下一轮请求；
- handler 异常 message 与完整路径进入 Session/model；
- 原异常对象进入 logger；
- 未注册工具原始 name 进入安全 ToolCall 与各出口；
- 原有抛异常测试仍要求回显“模拟工具故障”。

### LoomXTools 真实 handler

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~GetProvider不存在_真实Handler不回显原始Id" -- RunConfiguration.MaxCpuCount=1
```

首次结果：失败 1、通过 0；`private-header-value` 出现在 handler 返回内容。

## TDD：GREEN

### 残留 C1 定向回归

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~Handler返回普通失败|FullyQualifiedName~Handler抛出含敏感内容异常|FullyQualifiedName~未注册工具名含敏感内容|FullyQualifiedName~固定结构化安全失败|FullyQualifiedName~GetProvider不存在|FullyQualifiedName~FailureFactories" -- RunConfiguration.MaxCpuCount=1
```

结果：通过 6/6，0 失败。

覆盖内容：

- 普通不可信失败在 Session、ToolResult event/detail、UI、jsonl、下一轮真实 `ModelRequest` 中均无原文；
- handler 抛出的 secret、完整路径、inner exception 与 `Exception.Data` 不进入 logger exception/message/state、Session、事件或 model；仅异常类型保留；
- `loomx.get_provider` 真实 handler 不回显原始 id；
- 未注册工具名在 Session、事件、UI、jsonl、日志与下一轮请求中均替换为固定名称，且 call id 配对保持；
- TomlTools/AssistantTools 固定 JSON 错误码经过中央边界仍保留；
- `ToolResult.Fail` 默认不可信，`SafeFail` 显式可信。

### 安全相关分组回归

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~AgentLoopTests|FullyQualifiedName~ToolRegistryTests|FullyQualifiedName~LoomXToolsTests|FullyQualifiedName~TomlToolsTests|FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantSessionStoreTests|FullyQualifiedName~AssistantViewModelTests" -- RunConfiguration.MaxCpuCount=1
```

结果：通过 144/144，0 失败。

## 完整验证

### 完整 build

```powershell
dotnet build LoomX.slnx --no-restore --nologo
```

结果：成功，0 错误；7 个既有 warning（NU1903、CS8618、CA2024、CS8602），按约束未处理。

### 全量串行 tests

使用临时 `.runsettings` 设置 `MaxCpuCount=1`、`MaxParallelThreads=1`、`ParallelizeTestCollections=false`：

```powershell
dotnet test LoomX.slnx --no-build --no-restore --nologo --settings <临时串行配置>
```

结果：通过 926/926，0 失败，0 跳过。临时配置已删除。

### OpenSpec strict validate

```powershell
openspec validate structured-config-assistant-decisions --strict
```

结果：`Change 'structured-config-assistant-decisions' is valid`。

### Diff 检查

```powershell
git diff --check
```

结果：通过，无输出。

## 修改范围

生产代码仅修改允许范围内文件：

- `LoomX.Harness/ToolRegistry.cs`
- `LoomX.Harness/ToolArgumentSafety.cs`
- `LoomX.Harness/AgentLoop.cs`
- `LoomX/Assistant/LoomXTools.cs`
- `LoomX/Assistant/TomlTools.cs`
- `LoomX/Assistant/AssistantTools.cs`

测试：

- `LoomX.Tests/Assistant/AgentLoopTests.cs`
- `LoomX.Tests/Assistant/LoomXToolsTests.cs`
- `LoomX.Tests/Assistant/ToolRegistryTests.cs`

报告：

- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/whole-branch-fix-2-report.md`

没有修改数据库路径、OpenSpec/Comet 状态、既有审查报告、outputs 或其他 Session 产物。

## 结论

残留 Critical C1 已由中央 ToolResult 失败边界、固定异常日志边界、未知工具固定名称投影和 LoomXTools 固定失败映射完整覆盖。基于新增 RED/GREEN、144 项安全分组回归、926 项全量串行测试、完整 build、OpenSpec strict validate 与 diff 检查，C1 可以判定为完全关闭。
## 协调者独立复核

- 残留 C1 定向测试：6/6 通过。
- dotnet build LoomX.slnx --no-restore --nologo：0 错误，2 个既有 NU1903 warning。
- 首次仅设置 RunConfiguration.MaxCpuCount=1 的全量测试：925/926，通过外仅 GatewayViewModelDeletionTests.ComboDeletePreservesBoundComboAsSelectedMissingOption 因共享集合被并行修改失败；该测试单独重跑 1/1 通过。
- 使用 xUnit 串行 collection 配置重跑全量：926/926 通过，0 失败，0 跳过。
- OpenSpec strict validate 与 git diff --check 通过。
