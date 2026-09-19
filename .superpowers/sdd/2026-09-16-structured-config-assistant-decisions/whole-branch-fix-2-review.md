# structured-config-assistant-decisions 整分支修复 wave 2 scoped re-review

- 审查日期：2026-09-17
- 分支：`codex/structured-config-assistant-decisions`
- 基线：`9e482dfca39fd8761be0682672362a8d30d57583`
- 修复后：`415d905af5ce106d0276d20fbd48d6683fb1d028`
- 生产代码提交：`5708405aa18c64fd732a362dd7f0cd3ba5601b8a`
- Scoped package：`review-9e482df..415d905.diff`
- 审查范围：仅复核 `whole-branch-fix-1-review.md` 残留 Critical C1 是否完全关闭，并检查本次 micro-fix 是否引入新的 Critical/Important；未重新泛审已关闭项或分支其他既有实现。
- 结论：**Approved**
- **Ready to merge: Yes**

## Finding 统计

- Critical：**0**
- Important：**0**
- Minor：**0**

本轮没有需要列出的 finding。

## C1 复核结论

### 1. 普通 `ToolResult.Fail(raw)` 的中央安全边界：Addressed

- `LoomX.Harness/ToolRegistry.cs:17-41` 将 `ToolResult` 改为私有构造的 sealed record；`Fail` 固定创建不可信失败，`SafeFail` 才能显式标记固定安全失败，外部调用方不能通过构造器或属性伪造内部安全标记。
- `LoomX.Harness/AgentLoop.cs:226-243` 在失败内容进入 Session、`MessageCompleted`、`ToolCallCompleted.Detail`、UI、jsonl 与下一轮 `ModelRequest` 的共同入口前调用 `EnsureSafeFailure()`。普通 `Fail(raw)` 被固定替换为 `工具执行失败。`；成功结果保持原内容与 `Success=true` 语义。
- 新增回归 `LoomX.Tests/Assistant/AgentLoopTests.cs:428-473` 覆盖 Session、事件、UI、jsonl 和下一轮请求，确认普通失败原文不会离开执行边界。

结论：普通 `Fail` 无法保留原始失败文本；只有显式 `SafeFail` 可穿过中央边界，满足本轮要求。

### 2. 固定结构化错误与 LoomXTools 失败：Addressed

- `LoomX/Assistant/TomlTools.cs:383-389` 与 `LoomX/Assistant/AssistantTools.cs:421-427` 的固定结构化错误改用 `SafeFail`，`toml_operation_failed`、`invalid_arguments`、`invalid_request` 等错误码不会被中央泛化。
- `LoomX/Assistant/LoomXTools.cs:770-797` 的 Provider、Combo、Endpoint 查找异常不再拼接原始 id/name/key。
- `LoomX/Assistant/LoomXTools.cs:804-834` 将查找、参数和状态异常按类型映射为固定安全失败，不使用 `exception.Message`；header/path/value 等原始参数即使进入底层异常，也不会作为 ToolResult 回显。
- `LoomX.Tests/Assistant/LoomXToolsTests.cs:327-337` 覆盖真实 `loomx.get_provider` handler 的不存在路径，确认原始 id 不回显。

结论：TomlTools/AssistantTools 的结构化恢复语义保留；LoomXTools 的查找、参数和状态失败不再回显原始标识或参数。

### 3. handler 未捕获异常的日志与所有出口：Addressed

- `LoomX.Harness/AgentLoop.cs:420-428` 不再将原异常对象、message、inner exception 或 `Exception.Data` 交给 logger，也不再把 `exception.Message` 写入 ToolResult。
- 日志异常对象改为固定 `InvalidOperationException("工具处理器执行失败。")`；结构化状态只保留注册工具规范名称与异常类型全名。ToolResult 固定为 `工具执行失败。`。
- `LoomX.Tests/Assistant/AgentLoopTests.cs:475-516` 使用含敏感 message、完整路径、inner exception 与 `Data` 的自定义异常，验证 logger exception/message/state、Session、事件和下一轮请求均不含原始内容。Session 中固定结果继续覆盖 UI 与 jsonl 的共同数据源。

结论：未捕获 handler 异常只保留异常类型和固定安全异常，未发现第二条原异常回流路径。

### 4. 未注册工具名固定投影与协议配对：Addressed

- `LoomX.Harness/ToolArgumentSafety.cs:8-40` 对未注册工具统一投影为 `unknown.tool`，同时隐藏 arguments；已注册工具使用 `ToolDefinition.Name` 作为规范名称。
- `LoomX.Harness/AgentLoop.cs:156-184,226-243,390-428` 中原始 name 只用于本轮 registry 查找和执行选择；Session、事件、Tool 消息、UI、jsonl、下一轮请求和日志均使用安全名称。未知工具日志不记录原始 name。
- `ToolCall.Id` 在投影中保持不变，`ChatMessage.ToolResult` 与后续模型请求仍使用同一 id。
- `LoomX.Tests/Assistant/AgentLoopTests.cs:518-570` 覆盖敏感未知工具名的全出口与 call id 配对。

结论：未知工具名泄漏关闭，且未破坏 assistant tool call 与 Tool message 的 `tool_call_id` 配对。

### 5. 成功语义与 BrowserTools 降级：无新增 Critical/Important

- `ToolResult.Ok` 仍创建 `Success=true` 的结果；`EnsureSafeFailure()` 对成功结果直接返回原对象，未改变成功内容。
- `LoomX/Assistant/Browser/BrowserTools.cs:219-235` 仍使用普通 `Fail`，因此经 `AgentLoop` 后被有意泛化为固定失败。这是本次中央安全策略预期的保守降级，不是敏感信息泄漏。
- Browser 工具的成功结果、工具调用配对和失败状态仍保留；本轮未发现依赖原始 Browser 错误正文才能维持的核心协议状态机，因此不列 Important。

## 验证证据

### 独立定向回归

执行：

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-build --no-restore --nologo --filter "FullyQualifiedName~AgentLoopTests|FullyQualifiedName~ToolRegistryTests|FullyQualifiedName~LoomXToolsTests|FullyQualifiedName~TomlToolsTests|FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantSessionStoreTests|FullyQualifiedName~AssistantViewModelTests" -- RunConfiguration.MaxCpuCount=1
```

结果：**144/144 通过，0 失败，0 跳过**。

此外：

- `git diff --check 9e482df..415d905`：通过，无输出。
- 写入本审查报告前，测试后 `git status --short --branch` 仍为干净工作区；当前唯一工作区改动是本次获准写入的审查报告。
- 修复报告记录的完整验证为串行 **926/926**；本 reviewer 未重复生成临时 runsettings，而以独立 144 项安全相关回归和静态数据流复核作为本轮 scoped 验证。

## 最终 standalone 发布复核

输出目录：`outputs/2026-09-17-1822-structured-config-assistant-decisions`

独立读取结果：

- 目录创建时间：`2026-09-17 18:22:42 +08:00`，晚于生产提交 `5708405` 的提交时间 `2026-09-17 18:21:13 +08:00`，早于报告提交 `415d905`。
- `LoomX.exe` ProductVersion：`0.12.6+5708405aa18c64fd732a362dd7f0cd3ba5601b8a`
- `LoomX.dll` ProductVersion：`0.12.6+5708405aa18c64fd732a362dd7f0cd3ba5601b8a`
- `LoomX.exe` SHA-256：`E779B1EFB2B7D0C5D365A055C1BC415CEF410ADA9B2368600206C8F13ED7C3B3`
- `LoomX.dll` SHA-256：`F799FB1E2CE8F886BE26A6A8E8EA6419F34CD1541228A30811162C2DB347B9D0`
- `git diff 5708405..415d905 -- LoomX LoomX.Harness`：无生产代码差异，说明报告提交未改变已发布生产代码。
- 日志 `loomx-20260917.log` 存在 PID `14268` 的启动记录，进程路径、基目录与工作目录均精确指向该输出目录，并存在初始化完成记录。
- 当前 PID `14268` 已停止；既有 PID `28460` 仍运行于另一绝对路径，与报告所述隔离停止证据一致。

结论：发布版本、哈希、时间线、PID、Path 与日志证据相互一致，能够可信绑定生产代码提交 `5708405aa18c64fd732a362dd7f0cd3ba5601b8a`。

## 最终结论

残留 Critical C1 已完全关闭；本次 micro-fix 未引入新的 Critical 或 Important，亦未发现需要单列的 Minor。

**Approved**

**Ready to merge: Yes**
