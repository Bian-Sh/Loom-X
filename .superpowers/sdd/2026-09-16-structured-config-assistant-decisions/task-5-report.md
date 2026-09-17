# Task 5 实施报告：AskUser 领域模型与 UserDecisionBroker

日期：2026-09-16
分支：`codex/structured-config-assistant-decisions`

## 实施范围

本任务只创建以下文件，未修改项目文件、OpenSpec/Comet 状态文件或其他 Session 产物：

- `LoomX/Assistant/UserDecisions/UserDecisionModels.cs`
- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs`
- `LoomX.Tests/Assistant/UserDecisionModelsTests.cs`
- `LoomX.Tests/Assistant/UserDecisionBrokerTests.cs`
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-5-report.md`

SDK 默认编译通配符会自动接入新增 `.cs` 文件，因此不需要修改 `.csproj`。

## 关键设计

- 使用 `UserDecisionFieldType` enum 与 `UserDecisionField` 明确属性表达 `SingleSelect`、`MultiSelect`、`Number`、`Text` 四种字段，不让任意 `JsonObject` 进入 UI 领域模型。
- `UserDecisionRequest`、字段选项、默认多选值、提交结果字典及多选结果都复制为只读快照，避免调用方后续修改影响 pending 请求或已完成结果。
- `UserDecisionValidator` 集中校验请求与提交值；错误包含字段 id 和中文安全消息，不回显标题、问题、选项说明或用户提交内容。
- 标题、问题、说明、影响摘要、字段/选项展示文本和文本默认值均有长度约束；敏感模式识别复用 `SensitiveKeyPolicy`。
- 取消结果由 `Cancelled = true` 与空 `Values` 明确表示，不注入请求默认值。
- `UserDecisionBroker` 使用 `ConcurrentDictionary<string, PendingEntry>` 和不可预测 GUID request id。
- 每个 pending 使用 `TaskCompletionSource<UserDecisionResult>(TaskCreationOptions.RunContinuationsAsynchronously)`。
- Submit、Cancel、CancelOwner、调用方 CancellationToken、事件发布异常都先 `TryRemove`，再完成任务，随后释放 `CancellationTokenRegistration`。
- CancellationToken 注册采用带锁的延迟接管，覆盖“取消回调先于 registration 写入”的竞争窗口。
- 日志只记录 request id、owner id、字段/错误数量和异常类型；不记录请求展示内容、用户提交值、取消原因或事件订阅者异常原文。

## TDD 证据

### RED 1：缺少领域类型

命令：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecisionModelsTests|FullyQualifiedName~UserDecisionBrokerTests"
```

结果：退出码 1。测试项目编译失败，`LoomX.Assistant.UserDecisions`、`UserDecisionBroker`、`UserDecisionRequest` 等类型不存在。

### RED 2：模型行为失败

在只提供可编译 API 骨架、校验器尚未实现时运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecisionModelsTests"
```

结果：退出码 1；13 个测试中 12 个失败、1 个通过。失败覆盖重复 field id、空标题、未知默认选项、multi min/max、number 范围/步长、必填空文本、过长文本、敏感模式、展示文本长度和集合不可变性。

### GREEN 1：模型通过

同一模型定向命令结果：退出码 0；13/13 通过。

### RED 3：Broker 生命周期失败

在 Broker 仅发布事件但未维护 pending/完成通道时运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecisionBrokerTests"
```

结果：退出码 1；10/10 失败。失败覆盖 Submit/Cancel、CancellationToken、CancelOwner、重复完成、事件异常清理、并发 request id 和异步 continuation。

### GREEN 2：Broker 通过

同一 Broker 定向命令结果：退出码 0；10/10 通过。

### RED/GREEN 4：事件异常日志安全

自审发现直接记录事件订阅者异常可能把异常原文写入日志，因此先扩展测试记录异常文本：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecisionBrokerTests.事件订阅者抛异常_请求失败且不遗留Pending"
```

RED：退出码 1，日志中命中测试敏感文本。
GREEN：改为记录安全异常与异常类型后退出码 0，1/1 通过。

## 最终验证

### 定向测试

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecisionModelsTests|FullyQualifiedName~UserDecisionBrokerTests"
```

结果：退出码 0；23/23 通过，0 失败，0 跳过。

仅出现任务说明已声明无需批量处理的既有警告：`NU1903`、`CS8618`、`CA2024`、`CS8602`。

### 格式验证

```powershell
dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/UserDecisions/UserDecisionModels.cs LoomX/Assistant/UserDecisions/UserDecisionBroker.cs LoomX.Tests/Assistant/UserDecisionModelsTests.cs LoomX.Tests/Assistant/UserDecisionBrokerTests.cs
```

结果：退出码 0；未检测到格式差异。工作区加载提示存在警告，但未产生格式失败。

### Diff 校验

提交前执行：

```powershell
git diff --check
```

结果：退出码 0；未发现空白错误或冲突标记。

## 自审

- 已逐项核对任务简报中的模型与 Broker 测试矩阵。
- 没有记录用户自由文本、选择值、取消原因、prompt、API Key、Authorization、Header、正文或工具参数。
- 事件订阅者异常原文不会进入日志或对调用方暴露的异常链。
- 所有完成路径均先从并发字典移除；重复提交/取消返回 `false`。
- 取消结果的 `Values` 始终为空。
- 没有修改允许列表之外的实现、测试或流程状态文件。

## 风险与后续边界

- 本任务只提供领域模型与 Broker；DI 注册、`assistant.ask_user` 工具、AgentLoop/AssistantService 接入及 UI Dialog 属于后续任务。
- 未发现本任务范围内的未解决风险。
