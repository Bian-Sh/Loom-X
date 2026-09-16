# Task 6 修复轮 1 报告

日期：2026-09-17
基线：`237850f85f01f40d774dd1d8d04846aff2dfbe69`
分支：`codex/structured-config-assistant-decisions`

## 技术核对结论

审查报告的 4 项 finding 均可由当前代码路径与新增回归测试复现，未发现需要反驳的项目：

1. `AssistantTools.SerializeResult` 原样序列化全部提交值，`AgentLoop` 随后把结果加入 Session、发出 `MessageCompleted`，并传给下一轮模型；自由文本确实会跨越绑定边界。
2. 工具 Schema 虽声明 `additionalProperties:false`，运行时解析器仍会忽略未知属性；Task 5 通用敏感策略也不负责识别无凭据关键词的完整 TOML、JSON 正文和自定义 Header。
3. 一次性 `AsyncLocal` scope 建立在外层 async iterator 内，调用方后续 `MoveNextAsync` 恢复时 owner 会丢失；recording broker 实测 Request owner 与 finally 的 CancelOwner owner 不一致。
4. `assistant.ask_user` 未配置 `ToolDefinition.Timeout`，因此继承 30 秒默认值；人工等待会被通用工具超时截断。

## Finding 映射与修复

### 1. Critical：Text 原文泄漏

- `SerializeResult` 现在按请求字段类型生成结果：
  - `single_select` 保留选项 id；
  - `multi_select` 保留选项 id 数组；
  - `number` 保留数值；
  - `text` 仅返回固定结构 `{"provided":true|false}`，不返回、变形、编码或摘要原文。
- 集成测试使用非敏感自由文本标记，验证以下位置均不包含原文：
  - 直接 `ToolResult.Content`；
  - `AgentSession.Messages`；
  - `MessageCompleted` 事件消息；
  - 下一轮 `ModelRequest.Messages`。

### 2. Critical：AskUser 内容边界与属性契约

- 仅在 `AssistantTools` 内增加 AskUser 专用边界，没有扩大 Task 5 的 `SensitiveKeyPolicy`：
  - 拒绝完整 JSON object/array 正文；
  - 拒绝含 table header 与赋值、或至少两个赋值项的完整 TOML 块；
  - 拒绝常见 HTTP Header 与带连字符的 custom Header 行；
  - 显式校验顶层、field、option 的允许属性集合，使运行时与 `additionalProperties:false` 一致。
- 新增测试覆盖：
  - 无 credential 关键词的 TOML；
  - JSON 请求正文与响应正文；
  - `X-Tenant: acme`；
  - 顶层、field、option 未知属性携带敏感值；
  - 上述非法请求均未进入 pending；
  - 普通短句、单个合法选项和正常 JSON Schema 字段不会被误拒绝。

### 3. Important：Run owner 稳定传播

- 未修改 `AgentLoop`、`ToolRegistry`、`ApprovalHandler` 或 `ToolApprovalGate`。
- `AssistantService` 改为显式驱动 `AgentLoop` enumerator，并在每次实际 `MoveNextAsync` 外重建同一 owner scope；工具执行发生在该 scope 内。
- recording broker 测试验证同一次 Run 的 `RequestAsync` owner 与 finally `CancelOwner` owner 完全一致。

### 4. Important：人工等待不继承 30 秒工具超时

- `assistant.ask_user` 显式设置 `Timeout.InfiniteTimeSpan`，不再使用 `ToolDefinition` 的 30 秒默认值。
- 注册测试同时断言该定义不是 30 秒且为无限等待。
- 生命周期测试确认请求保持 pending，并可由 Run CancellationToken 在 2 秒测试窗口内立即结束；既有 Stop、新会话和外部取消测试继续通过。

## TDD 证据

### RED

新增/调整测试后、生产代码修改前运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests"
```

结果：失败，`11` 个失败、`24` 个通过、共 `35` 个。关键失败与 finding 一一对应：

- Text 结果仍是 JSON string，测试读取固定安全对象时报 `The node must be of type 'JsonObject'`；
- TOML、JSON 正文、Header 与未知属性进入 pending，测试的短取消令牌触发 `TaskCanceledException`；
- recording broker 显示 Request owner 与 finally CancelOwner owner 为两个不同 GUID；
- 工具定义仍是默认 30 秒，未满足无限等待断言。

### GREEN

最小生产修复后运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests"
```

结果：`35/35` 通过。

随后执行要求的定向回归：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AgentLoopTests"
```

结果：`52/52` 通过。

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecision|FullyQualifiedName~SensitiveKeyPolicyTests"
```

结果：`69/69` 通过。

```powershell
dotnet test LoomX.slnx --no-restore
```

结果：`844/844` 通过。

格式检查首次发现两份经脚本写回的 C# 文件缺少仓库要求的 UTF-8 BOM；核对真实文件内容未损坏后只恢复 BOM，没有重写中文内容。修正后：

```powershell
dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/AssistantTools.cs LoomX/Assistant/AssistantService.cs LoomX.Tests/Assistant/AssistantToolsTests.cs LoomX.Tests/Assistant/AssistantServiceTests.cs
```

结果：通过。

```powershell
git diff --check
```

结果：通过。

## 改动文件

- `LoomX/Assistant/AssistantTools.cs`
- `LoomX/Assistant/AssistantService.cs`
- `LoomX.Tests/Assistant/AssistantToolsTests.cs`
- `LoomX.Tests/Assistant/AssistantServiceTests.cs`
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-6-fix-1-report.md`

未修改 `LoomX.Harness/AgentLoop.cs`、`LoomX.Harness/ToolRegistry.cs`、`LoomXHost`、Comet/OpenSpec/计划状态文件或发布目录。

## 自审与剩余风险

- 内容识别刻意限定为明确的完整 JSON、TOML 块和 Header 形态，避免把普通短句、单个选项或 `JSON Schema` 字段说明误判；它不是通用 DLP，也不尝试摘要或改写禁止内容。
- 无限工具等待依赖既有 Run CancellationToken、`Cancel()`、新会话与 finally `CancelOwner` 收敛；这些路径均有回归测试。
- 保留既有 `NU1903`、`CS8618`、`CA2024`、`CS8602` 警告，未在本任务中处理。
- `git pull --ff-only` 显示远端已最新，但共享 Git 对象仍报告既有 geometric repack 异常；未清理或改写共享 `.git` 数据。
- 提交消息固定为 `修复 AskUser 工具安全与生命周期`。最终本地 HEAD 与远端 SHA 在提交推送后由交付信息核对；提交对象无法在自身内容中记录自己的 SHA。