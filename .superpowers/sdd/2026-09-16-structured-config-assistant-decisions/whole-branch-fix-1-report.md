# structured-config-assistant-decisions 整分支修复 wave 1 报告

- 日期：2026-09-17
- 分支：`codex/structured-config-assistant-decisions`
- 起始 HEAD：`9681855ae6bfb0c79657c5877abf30456ad77986`
- 范围：仅修复 `whole-branch-review.md` 的 C1、C2、I1、I2、I3、I4、M1
- 约束：未修改数据库路径、计划勾选、OpenSpec tasks、Comet 状态或首轮审查报告；已从生产代码提交 HEAD 完成 standalone publish 验证；本修复会话未 commit、未 push

## 调试方法

每条 finding 均先按 `systematic-debugging` 反向追踪数据/状态流，再用自动化测试固定可复现行为。生产代码只在对应 RED 被观察后修改，随后执行最小 GREEN 和分组回归。

## Finding 关闭说明

### C1 工具参数安全边界

**根因**

`AgentLoop` 把模型返回的原始 `ToolCall` 同时用于 handler 执行、Session、`AgentEvent`、审批、UI 和后续 `ModelRequest`，`ToolDefinition` 没有安全参数投影契约；持久化与 UI 又默认信任 `ArgumentsJson`。因此 handler 内校验发生得太晚，拒绝前原文已经扩散。

**设计裁决**

- `ToolDefinition.SafeArgumentsProjector` 定义工具专用安全投影。
- `AgentLoop` 保留原始调用仅用于本轮 handler 执行；写入会话、事件、审批及后续模型请求的是投影后的 `ToolCall`。
- TOML 投影仅保留操作、文件名安全摘要、不可逆 SHA-256 路径摘要、键路径段数/SHA-256、操作数量；不保留完整路径、key 原文或 value。
- AskUser 投影仅保留字段数量、字段类型、必填、选项数量和 `allow_cancel`；不保留 title/question/reason/impact/label/description/default 文案或值。
- 未注册工具、旧工具无 projector、JSON 无效或 projector 抛出任意异常时统一替换为固定隐藏投影，绝不回退原文。
- `ToolCall.ArgumentsAreSafe` 标记安全来源；`AgentSession`、`AssistantSessionStore`、`OpenAiCompatibleModelClient`、UI ViewModel 均做纵深防御。旧 jsonl 没有安全标记时按不可信参数隐藏。
- handler 仍收到原始 JSON，真实链路测试确认完整路径和 Header value 正常到达 TOML service。

**RED**

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UnknownTool_原始参数|FullyQualifiedName~Patch_真实链路仅保存安全投影|FullyQualifiedName~AskUser_Handler拒绝前Session事件"
```

结果：失败 3、通过 0；原始路径、`private-header-value`、AskUser Bearer/TOML 文本分别出现在 Session、审批详情、MessageCompleted、jsonl 和下一轮请求。

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~SafeArgumentsProjector异常时安全失败"
```

结果：失败 1、通过 0；projector 的 `ApplicationException` 直接逃逸。

**GREEN**

同命令结果分别为：通过 3/3；通过 1/1。另增加旧 jsonl 与未标记 UI 参数回归，均通过 2/2。

### C2 `toml.get` 脱敏

**根因**

`SensitiveKeyPolicy.Redact` 只按 key path 脱敏，非敏感 key 下的 string scalar 不执行内容检测；`headers`、`custom_headers`、`http_headers` 也未作为敏感容器。

**设计裁决**

- Header 容器名及其所有后代均为敏感路径。
- 数组、对象、普通 table、inline table、AoT 统一递归。
- 所有 string scalar 复用 `ContainsSensitiveContent`，Bearer、JWT、常见 API key/token 形态统一替换为 `***`。
- 普通非敏感字符串保持原值，避免无谓破坏可用结果。

**RED**

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~SensitiveKeyPolicyTests"
```

结果：失败 7、通过 25；Header 容器、Bearer/JWT/API key 和 benign-key-secret-value 均可穿透。

**GREEN**

同命令：通过 32/32。追加真实 TOML 数组/inline table/table/AoT/Header 回归：通过 1/1。

### I1 TOML commit point

**根因**

原子 Replace/Move 成功后仍把调用方 token 传给目标解析和恢复；提交后的取消会抛 `OperationCanceledException`，但磁盘已经改变。

**设计裁决**

- commit 前所有读取、临时写入、临时验证、重试和原子提交入口继续尊重调用方取消。
- Replace/Move 成功即进入 commit 后阶段；目标验证与必要恢复统一使用 `CancellationToken.None`，最终结果与磁盘一致。

**RED/GREEN**

与 I2/M1 合并的 `Wave1_` 测试组：RED 时 Replace 后取消、Move 后取消、恢复阶段原 token 取消均抛取消；GREEN 后三项均确定性通过。

### I2 并发 lost update

**根因**

`PatchAsync` 从读取、候选构建到提交没有路径级串行化；备份和替换前也不比较源版本，跨 service 实例和外部编辑会静默覆盖。

**设计裁决**

- 新增静态、引用计数、可回收的 per-path async lock；key 使用规范化绝对路径和 `OrdinalIgnoreCase`，跨 service 实例生效并覆盖 Windows 大小写等价路径。
- 从读取源开始持锁直到提交/失败结束；不同路径使用不同 semaphore。
- 读取时保存源是否存在、UTF-8 BOM 和 SHA-256 fingerprint；备份前及原子提交前再次比较。冲突返回安全 `SourceChanged` 结构，不覆盖新版本。

**RED/GREEN**

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~Wave1_"
```

首次结果：失败 9、通过 2，共 11；同路径两个 Patch、大小写等价路径和外部修改均复现 lost update。GREEN：通过 11/11；不同路径并行测试也通过。

### I3 AskUser 无 claim 收敛

**根因**

Broker 只检查事件 delegate 快照是否非空；同步调用完成后不检查 pending 是否已 claim。旧 delegate/inactive/busy handler 返回后会留下无限 pending。

**设计裁决**

- 事件同步发布完成后，Broker 原子检查 pending 的 claim 状态；仍无人 claim 时立即移除、释放取消注册并以安全 `InvalidOperationException` 失败。
- 生产 ViewModel 保持“active/available 检查与 `TryClaim` 在同一锁内、返回 handler 前完成 claim，再调度 UI”的既有正确模式。
- 测试订阅者同步 claim；无 claim、snapshot→unsubscribe→旧 handler、多个 VM、Deactivate/Dispose/Submit 竞态均验证收敛。

**RED**

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~订阅者返回前未Claim|FullyQualifiedName~事件快照后订阅者解除"
```

结果：失败 2、通过 0；两项均等待 1 秒后 `TimeoutException`。

**GREEN**

同命令：通过 2/2。UserDecision/Broker/AssistantTools/ViewModel/UI 生命周期扩展组通过 114/114。

### I4 AskUser Schema/运行时一致性

**根因**

`options` 与 `default_option_ids` 用 `as JsonArray ?? empty`，把错误类型静默当作未提供；其他 optional 属性由 `GetValue`/`Deserialize` 抛出非领域异常，最终变成 `ask_user_failed` 而非 `invalid_request`；显式 null 也被当缺省。

**设计裁决**

- optional 属性不存在才使用缺省；只要属性存在（包括 null）就严格验证 JSON 类型。
- 字符串、布尔、整数、number、数组和数组元素统一转换为 `UserDecisionValidationException`，工具统一返回安全 `invalid_request`。
- 参数化覆盖 request、field、option 的全部已知 optional 属性以及 array/string/number/bool/object/null 典型错误；保留 unknown property 回归。

**RED**

```powershell
dotnet test LoomX.Tests\LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AskUser_已知Optional属性存在时错误Json类型统一返回InvalidRequest"
```

行为 RED：17/17 失败，主要实际结果为 `ask_user_failed`，`options`/`default_option_ids` 还会被静默接受。追加 null 后 RED：失败 2、通过 17。

**GREEN**

同命令最终：通过 19/19。

### M1 `formatting_changed` / BOM

**根因**

读取 UTF-8 BOM 时会跳过 BOM，但临时文件始终使用无 BOM `UTF8Encoding` 写入；公开结果仍固定 `FormattingChanged=false`。

**设计裁决**

- 源快照记录 UTF-8 BOM，真实写入使用与源一致的 UTF-8 encoding。
- set/delete/patch 均保留 BOM；no-op 不触碰字节；新文件继续无 BOM。
- 在当前支持的 UTF-8 输入边界内不再产生不可避免的编码格式变化，因此 `FormattingChanged=false` 与真实字节一致。

**RED/GREEN**

`Wave1_` 首次 RED 中 BOM set/delete/patch 三项均失败；GREEN 后 BOM set/delete/patch/no-op 字节级测试全部通过。

## 修改文件与理由

### 生产代码

- `LoomX.Harness/ToolRegistry.cs`：增加安全参数 projector 契约。
- `LoomX.Harness/ToolArgumentSafety.cs`：集中执行 projector、未知/失败安全回退和安全标记纵深防御。
- `LoomX.Harness/AgentLoop.cs`：原始调用只交给 handler；Session/事件/审批使用安全调用。
- `LoomX.Harness/ChatMessage.cs`：为安全参数增加来源标记。
- `LoomX.Harness/AgentSession.cs`：拒绝未标记工具参数进入内存会话。
- `LoomX/Assistant/AssistantSessionStore.cs`：持久化安全标记；旧 jsonl 未标记参数隐藏后恢复。
- `LoomX/Assistant/OpenAiCompatibleModelClient.cs`：构建实际请求时再次拒绝未标记参数。
- `LoomX/ViewModels/AssistantViewModel.cs`：UI 详情只接受安全参数。
- `LoomX/Assistant/TomlTools.cs`：TOML 专用不可逆结构投影。
- `LoomX/Assistant/AssistantTools.cs`：AskUser 结构投影与 optional JSON 严格类型解析。
- `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs`：Header 容器与所有 string scalar 内容级脱敏。
- `LoomX/Assistant/Configuration/TomlDocumentService.cs`：commit point、fingerprint CAS、BOM 保留和路径锁接入。
- `LoomX/Assistant/Configuration/TomlPathLockPool.cs`：跨 service、大小写等价、引用计数可回收的 per-path async lock。
- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs`：发布后无人 claim 立即收敛。

### 测试代码

- `LoomX.Tests/Assistant/AgentLoopTests.cs`：未知工具、projector 失败、安全 Session/ModelRequest 与 handler 原参。
- `LoomX.Tests/Assistant/TomlToolsTests.cs`：TOML 完整安全链路、审批、jsonl 与 handler 原参。
- `LoomX.Tests/Assistant/AssistantToolsTests.cs`：AskUser 拒绝前投影、全部 optional wrong-type/null。
- `LoomX.Tests/Assistant/SensitiveKeyPolicyTests.cs`：Header 容器、Bearer/JWT/API key、普通值。
- `LoomX.Tests/Assistant/TomlDocumentServiceTests.cs`：commit point、并发/CAS、不同路径、BOM 字节、复合 TOML 脱敏。
- `LoomX.Tests/Assistant/UserDecisionBrokerTests.cs`：无 claim、旧 delegate、claim/submit/dispose 竞态。
- `LoomX.Tests/Assistant/UserDecisionBrokerTestExtensions.cs`：测试 UI 同步 claim 约定。
- `LoomX.Tests/Assistant/AssistantServiceTests.cs`：测试 handler 在返回前 claim。
- `LoomX.Tests/Assistant/AssistantSessionStoreTests.cs`：旧 jsonl 不可信参数迁移隐藏。
- `LoomX.Tests/Assistant/AssistantViewModelTests.cs`：安全参数展示与旧参数隐藏。
- `LoomX.Tests/Assistant/OpenAiCompatibleModelClientTests.cs`：安全 projector 后的结构化参数断言。

## 最终验证

- `dotnet test` 安全链路定向（AgentLoop/SessionStore/AssistantService/OpenAI model request）：100/100 通过。
- `dotnet test` TOML 全组（TomlModels/SensitiveKeyPolicy/TomlDocumentService/TomlTools）：136/136 通过。
- `dotnet test` UserDecision/Broker/AssistantTools/ViewModel/UI 生命周期：115/115 通过。
- Task 8 扩展定向集合：260/260 通过。
- `dotnet build LoomX.slnx --no-restore`：0 error；仅既有 NU1903 警告。
- `dotnet test LoomX.slnx --no-build`：920/920 通过。
- `openspec validate structured-config-assistant-decisions --strict`：valid。
- `git diff --check`：通过，无输出。

## 剩余风险

- 文件 fingerprint compare 与真正的 OS 原子替换之间仍存在外部进程极短竞争窗口；仓库内跨实例竞争已由 per-path lock 消除，提交前也执行二次 fingerprint 检查。若未来要求对任意外部编辑器做到严格 CAS，需要引入平台级文件锁或带版本语义的存储协议。
- 无 projector 的旧/第三方工具会隐藏全部参数而不是保留细节，这是故意的安全失败；若某工具需要在 UI/后续模型中显示非敏感结构，应显式增加专用 projector。
- 未处理用户明确排除的既有 NU1903、CS8618、CA2024、CS8602 和全仓 formatter 基线。
- standalone 发布已从生产代码提交 HEAD `2f0f315c3f16fad706f278af0ea2887ceaadae1d` 完成验证；本修复会话未 commit、未 push。

## Standalone 发布验证

- 验证日期：`2026-09-17`
- 生产代码 HEAD：`2f0f315c3f16fad706f278af0ea2887ceaadae1d`
- 输出目录：`D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1732-structured-config-assistant-decisions`
- 发布前确认目标目录不存在；未覆盖、删除或移动任何既有 `outputs` 目录。

### 发布命令与版本

```powershell
dotnet publish LoomX\LoomX.csproj -c Release -r win-x64 --self-contained true -o "D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1732-structured-config-assistant-decisions" -p:SourceRevisionId=2f0f315c3f16fad706f278af0ea2887ceaadae1d
```

发布成功；仅出现既有 NU1903、CS8618、CA2024 警告。

- `LoomX.dll` ProductVersion：`0.12.6+2f0f315c3f16fad706f278af0ea2887ceaadae1d`
- `LoomX.exe` ProductVersion：`0.12.6+2f0f315c3f16fad706f278af0ea2887ceaadae1d`
- `LoomX.dll` SHA-256：`0125513B880D2748CAED3345A8F692A72CB42001B162B42E7F93E0ADD36227D9`
- `LoomX.exe` SHA-256：`EEC1F27266A11E6673ED4962810C8931A9FFB2EF80632F9A8BA00EEF1DC01C6B`

### 启动命令与进程验证

```powershell
Start-Process -FilePath "D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1732-structured-config-assistant-decisions\LoomX.exe" -ArgumentList "--allow-multiple-instances" -WorkingDirectory "D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1732-structured-config-assistant-decisions" -WindowStyle Hidden -PassThru
```

- 启动时间：`2026-09-17 17:33:35.592 +08:00`
- 本轮 PID：`43340`
- 等待：`12` 秒
- 等待后 PID `43340` 仍存活且 `Responding=true`。
- `Process.Path`：`D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1732-structured-config-assistant-decisions\LoomX.exe`
- `Process.Path` 与本轮绝对 exe 路径精确匹配：`True`。

启动前 PID 清单：

- PID `28460`：`D:\AppData\Github\Loom-X\outputs\LoomX-win-x64-2026-09-17-activity-scrollbar-right\LoomX.exe`

启动后 PID 清单：

- PID `28460`：既有实例，路径未变。
- PID `43340`：本轮 standalone 实例。

### 日志证据

日志文件：`C:\Users\BianShanghai\AppData\Local\LoomX\logs\loomx-20260917.log`

```text
2026-09-17 17:33:36.290 +08:00 [WRN] LoomX.App 调试启动已允许多个桌面实例，进程 43340
2026-09-17 17:33:36.321 +08:00 [INF] LoomX.App 桌面应用启动，进程 43340，用户 "BianShanghai"，进程路径 "D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1732-structured-config-assistant-decisions\LoomX.exe"，基目录 "D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1732-structured-config-assistant-decisions\"，启动工作目录 "D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1732-structured-config-assistant-decisions"，规范化工作目录 "D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1732-structured-config-assistant-decisions"
2026-09-17 17:33:38.878 +08:00 [INF] LoomX.ViewModels.MainWindowViewModel 概览刷新完成 6 个 Provider、18 个模型、3 个 Endpoint、13 条路由，网关状态 Stopped，配置库 "C:\Users\BianShanghai\AppData\Local\LoomX\LoomX.db"，进程 43340
```

- PID `43340` 的“调试启动已允许多个桌面实例”：`1` 条。
- PID `43340` 的“桌面应用启动”：`1` 条。
- PID `43340` 的后续“概览刷新完成”初始化证据：`1` 条。
- PID `43340` 的“检测到已有实例”“Shell bootstrap 创建/子进程失败”日志：`0` 条。

### 停止与隔离验证

```powershell
Stop-Process -Id 43340
Start-Sleep -Seconds 5
```

- 停止命令发送时间：`2026-09-17 17:34:26.011 +08:00`。
- 延迟 `5` 秒后 PID `43340` 已消失。
- 既有 PID `28460` 在停止前后均存活，路径未变。
- 仅停止本轮 PID；其他既有 LoomX 实例未被停止。
