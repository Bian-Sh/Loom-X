# Task 6 修复轮 2 报告

日期：2026-09-16
基线：`65b80b13abf70ba58267de11f5ad7e1f47d3cd2b`
分支：`codex/structured-config-assistant-decisions`

## Finding 核对

复审剩余 finding 可在当前代码中确认：`LooksLikeHttpHeader` 虽先要求冒号前只含字母、数字或连字符，但最终仅接受“包含连字符”或短 allowlist 中的名称。因此 `Server`、`Date`、`Location`、`Tenant` 这类无连字符 Header 名会返回 `false`，请求继续进入 `IUserDecisionBroker` pending。

修复采用 AskUser 边界内的最小改动：移除不完整的 Header allowlist，改为按 HTTP field-name 的 ASCII token 字符集合判定冒号前的单 token 名称。名称中含空格的普通短句不会满足该判定，因此 `Please choose: option A`、普通原因文本和带空格的合法选项仍可进入 pending。非法请求仍在 `ParseRequest` 完成前置内容校验时失败，返回固定 `invalid_request`，不会回显 Header 值。

## TDD 证据

### RED

仅修改测试后运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~AssistantToolsTests
```

结果：失败 `4`、通过 `15`、共 `19`。新增的 `Server: nginx`、`Date: Wed, 16 Sep 2026 12:00:00 GMT`、`Location: /next`、`Tenant: acme` 均进入 pending，随后由测试取消令牌触发 `TaskCanceledException`；失败原因与 finding 一致。新增的带空格普通短句正向测试在 RED 阶段通过。

### GREEN

最小实现后再次运行同一命令：通过 `19/19`。首次 GREEN 尝试暴露 apostrophe token 字符的 C# 字面量拼写错误；仅修正该实现语法后重新运行得到上述通过结果。

## 回归验证

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AgentLoopTests"
```

结果：通过 `57/57`。

```powershell
dotnet test LoomX.slnx --no-restore
```

结果：通过 `849/849`。

```powershell
dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/AssistantTools.cs LoomX.Tests/Assistant/AssistantToolsTests.cs
```

结果：通过；仅输出既有工作区加载警告提示。

```powershell
git diff --check
```

结果：通过。

测试输出保留既有 `NU1903`、`CS8618`、`CA2024`、`CS8602` 警告，本修复未扩大范围处理。

## 改动

- `LoomX/Assistant/AssistantTools.cs`
  - 删除不完整的 `KnownHttpHeaders` allowlist。
  - 将 Header 名判定封闭为单 token ASCII HTTP field-name 字符。
- `LoomX.Tests/Assistant/AssistantToolsTests.cs`
  - 增加 `Server`、`Date`、`Location`、`Tenant` 不进入 pending 的回归用例。
  - 增加 `Please choose: option A`、普通原因文本、带空格合法选项仍可进入 pending 的正向用例。
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-6-fix-2-report.md`
  - 记录本轮核对、TDD 与验证证据。

## 自审

- Header 值只参与布尔边界判定；失败结果仍使用固定安全消息，不包含原始 Header 名或值。
- 单 token 判定只接受 ASCII HTTP token 字符；包含空格或非 ASCII 的普通自然语言前缀不会被当作 Header。
- 未修改 Text 安全序列化、Run owner、无限等待超时、审批语义、Comet/OpenSpec 或计划状态。
- 未修改允许清单之外的生产、测试或流程文件。
- `git pull --ff-only` 显示远端已最新，同时仍报告既有 geometric repack 对象异常；未清理或改写共享 Git 数据。