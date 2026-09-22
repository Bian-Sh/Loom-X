# Task 6 实施报告：`assistant.ask_user` 与会话恢复

日期：2026-09-16

## 实现范围

- 新增 `AssistantTools.RegisterAll(ToolRegistry registry, IUserDecisionBroker broker)`。
- 注册只读工具 `assistant.ask_user`，提供结构化 Schema、强类型请求转换、提交/取消 JSON 结果与安全失败结果。
- 通过运行级 owner 上下文把 AskUser pending request 归属到单次 Assistant Run。
- `AssistantService` 在取消、新会话、外部取消令牌及 `finally` 路径收敛 pending request。
- `LoomXHost` 注册 singleton `IUserDecisionBroker` 并把 AskUser 工具接入共享 `ToolRegistry`。
- 保持 `ApprovalHandler` / `ToolApprovalGate` 既有语义不变。

## TDD RED 证据

1. 首次新增 `AssistantToolsTests` 后运行：

   ```powershell
   dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~AssistantToolsTests
   ```

   结果：失败。编译器报告 `CS0103`，`AssistantTools` 尚不存在。

2. 新增 `AssistantService` AskUser 集成测试后运行：

   ```powershell
   dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~AssistantServiceTests
   ```

   结果：失败。编译器报告 `CS1729`，`AssistantService` 尚未注入 `IUserDecisionBroker`。

3. 补充四类字段转换测试后运行 `AssistantToolsTests`：

   结果：失败。整数 JSON 数值不能直接按 `decimal` 读取，请求未进入 pending，测试超时。随后改为基于 JSON 反序列化的数值转换。

4. 补充安全失败结果必须为结构化 JSON 的断言后运行 `AssistantToolsTests`：

   结果：失败。原失败结果是普通文本，`JsonNode.Parse` 抛出 `JsonReaderException`。随后统一为不回显原始输入的安全 JSON 错误。

## GREEN 与回归验证

- `AssistantToolsTests`：6/6 通过。
- `AssistantServiceTests`：20/20 通过。
- 指定 Assistant/AgentLoop 定向集：43/43 通过。
- UserDecision 与敏感策略定向集：69/69 通过。
- 完整解决方案测试：835/835 通过。
- 指定文件 `dotnet format --verify-no-changes`：通过。
- `git diff --check`：通过。

验证命令：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AgentLoopTests"
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecision|FullyQualifiedName~SensitiveKeyPolicyTests"
dotnet test LoomX.slnx --no-restore
dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/AssistantTools.cs LoomX/Assistant/AssistantService.cs LoomX/LoomXHost.cs LoomX.Tests/Assistant/AssistantToolsTests.cs LoomX.Tests/Assistant/AssistantServiceTests.cs
git diff --check
```

## 安全边界

- 敏感请求在 Broker pending 发布前失败。
- 工具失败结果不回显原始参数或敏感文本。
- 取消结果只返回 `cancelled` 与空 `values`，不返回取消原因。
- 文本提交继续复用 `UserDecisionValidator` 的内容级敏感检测。
- 未新增包含 owner id、取消原因、用户选择值或自由文本的日志。

## 改动文件

- `LoomX/Assistant/AssistantTools.cs`
- `LoomX/Assistant/AssistantService.cs`
- `LoomX/LoomXHost.cs`
- `LoomX.Tests/Assistant/AssistantToolsTests.cs`
- `LoomX.Tests/Assistant/AssistantServiceTests.cs`
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-6-report.md`

## 提交与推送

提交消息固定为 `接入 AskUser 工具与会话恢复`。最终提交 SHA、远端 SHA 与推送结果在提交完成后由最终交付信息核对；提交对象无法可靠地在自身文件内容中记录自己的最终 SHA。

## 剩余风险与约束

- 保留并未处理既有 `NU1903`、`CS8618`、`CA2024`、`CS8602` 警告。
- `git pull --ff-only` 显示远端已是最新，但同时报告共享 Git 对象的 geometric repack 异常；按任务约束未修复或清理共享 `.git` 元数据。
- 本任务明确限制写入范围，未创建 `outputs/` 发布包，也未修改任何 Comet/OpenSpec 状态文件。
