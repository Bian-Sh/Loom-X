# structured-config-assistant-decisions 整分支修复 wave 1 scoped re-review

- 审查日期：2026-09-17
- 分支：`codex/structured-config-assistant-decisions`
- 基线：`9681855ae6bfb0c79657c5877abf30456ad77986`
- 修复后：`e624dfb03346f484ab5d72108addc724887ceccf`
- 生产代码提交：`2f0f315c3f16fad706f278af0ea2887ceaadae1d`
- 审查方式：只复核首轮 C1、C2、I1、I2、I3、I4、M1，并检查修复 diff 是否引入新的 Critical/Important；未重新泛审旧实现。
- 结论：**Changes requested**
- **Ready to merge: No**

## Finding 统计

- Critical：**1**
- Important：**0**
- Minor：**0**
- 新增独立 Critical/Important：**0**；当前阻塞项是首轮 C1 尚未完全关闭。

## 总结

C2、I1、I2、I3、I4、M1 均已按首轮要求关闭，standalone 发布证据也可复核。C1 的 tool-call 参数投影主链已显著加固：未知工具、缺少 projector、projector 异常、旧 jsonl 无 safe marker、审批前路径、Session/UI/后续 ModelRequest 等均安全失败；但是**原始工具参数仍可通过内置 handler 的失败消息，以及未捕获 handler 异常，重新进入 ToolResult、Session、UI、jsonl、后续 ModelRequest 或日志**。因此“原始参数仅短生命周期给 handler”的边界仍不成立，不能批准合并。

## 首轮 finding 复核

### C1 原始工具参数安全投影：**Not addressed（Critical）**

#### 已关闭的部分

- `LoomX.Harness/AgentLoop.cs:156-166` 在写入 Assistant 消息、Blocks 和 `MessageCompleted` 前创建安全 `ToolCall`；原始调用只保留给本轮执行路径。
- `LoomX.Harness/AgentLoop.cs:176-233` 的 `ToolCallStarted`、审批事件、审批 gate、拒绝结果和正常工具结果均使用安全调用的 id/name；审批摘要来自安全参数投影。
- `LoomX.Harness/ToolArgumentSafety.cs:10-33` 对未知工具、projector 缺失、无效 JSON、projector 抛异常统一返回固定隐藏投影，不回退原始参数。
- `LoomX.Harness/AgentSession.cs:56,76`、`LoomX/Assistant/AssistantSessionStore.cs:118-138`、`LoomX/Assistant/OpenAiCompatibleModelClient.cs:657-674`、`LoomX/ViewModels/AssistantViewModel.cs:1621-1672` 对 Session、持久化、后续模型请求和 UI 又执行 `EnsureSafe`。
- `LoomX/Assistant/AssistantSessionStore.cs:272-297` 对旧 jsonl：缺少 `arguments_safe=true` 的 tool call 在 `RestoreMessage` 时会被固定隐藏。正常应用写入的 safe marker 可维持投影；本轮没有发现正常应用路径可让模型直接设置该 marker。
- TOML projector 不保留 value/完整路径/key 原文；AskUser projector 不保留 title/question/reason/impact/label/description/default 文案或值。拒绝前 Session/Event 路径已覆盖。

#### 仍然可触发的泄漏路径

`ToolCall.ArgumentsJson` 本身虽已投影，但 handler 返回的失败文本仍被当作可信安全内容，内置工具会把模型提供的原始参数直接拼进失败结果：

1. 模型调用 `loomx.get_provider`，参数例如：

   ```json
   {"id":"private-header-value"}
   ```

2. `loomx.get_provider` 没有 projector，tool-call 参数会按预期隐藏；但 `LoomX/Assistant/LoomXTools.cs:166-170` 仍把原始 `id` 交给 handler。
3. `LoomX/Assistant/LoomXTools.cs:770-775` 在找不到 Provider 时，把原始 `idOrBusinessId` 拼进 `KeyNotFoundException`。
4. `LoomX/Assistant/LoomXTools.cs:804-812` 将 `exception.Message` 原样变成 `ToolResult.Fail`。
5. `LoomX.Harness/AgentLoop.cs:226-241` 将该 `ToolResult.Content` 写入 Session，并产生 ToolResult UI/事件；下一轮 `ModelRequest` 会携带该 Tool 消息，`AssistantSessionStore` 也会持久化 message content。`private-header-value` 不命中当前 `SecretLeakScan` 的 `sk-`/Bearer 兜底，因此 jsonl 也可落盘。

此外，任一未自行捕获异常的 handler 若在异常消息中包含原始参数，`LoomX.Harness/AgentLoop.cs:419-422` 会把原异常对象写入日志，并把 `exception.Message` 放入 ToolResult。这直接违反本次复核要求中的“日志均为安全投影”。

#### 影响

- 模型可把任意自由字符串放进 `id`/name/key 等参数，并借失败路径使其进入 Session、UI、jsonl 和后续模型请求；这包括不符合 `sk-`/Bearer 特征的 Header value、内部标识或用户路径片段。
- handler 异常还可把同类内容写入 Serilog。
- 这不是 safe marker 的伪造问题，而是安全参数投影之后存在第二条未受控的返回/异常通道，所以现有 `ArgumentsAreSafe` 纵深防御无法拦截。

#### 建议

- 内置工具的 Guard/查找失败不得回显模型提供的原始参数；返回稳定错误码和固定安全消息，例如“Provider 不存在”。
- `AgentLoop` 捕获 handler 异常时记录固定安全异常对象和异常类型，不记录原始异常正文；ToolResult 返回固定安全消息。
- 若要从架构上覆盖所有工具，给 ToolResult 增加与参数投影同等级的安全来源/投影边界，或在进入 Session/Event/UI/jsonl/ModelRequest 前统一安全化，而不能仅依赖“handler 自觉保证 Content 安全”的注释契约。
- 增加回归：无 projector 的内置工具以普通非特征 secret 作为无效 id；断言 ToolResult、Session、Event、UI、jsonl、下一轮真实 ModelRequest 和日志均不含原文。

### C2 `toml.get` Header 后代与 string scalar 脱敏：**Addressed**

- `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:14-31` 将 `headers`、`custom_headers`、`http_headers` 纳入敏感路径；任意后代路径都会命中。
- `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:98-120` 在所有 string scalar 上执行内容检测；数组先递归、对象再递归，避免只检查键名。
- `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:162-185` 对数组和对象递归传递当前/子路径，因此普通数组、inline table、普通 table、AoT 与 Header 容器使用同一脱敏规则。
- `LoomX.Tests/Assistant/TomlDocumentServiceTests.cs:207-248` 覆盖普通数组、inline table、普通 table/AoT、Header 容器及普通值；`SensitiveKeyPolicyTests.cs:90-149` 覆盖任意 Header 名、Bearer/JWT/API key 和非敏感普通标量。
- 普通非敏感字符串保持原值；Header 容器下的值全部变为 `***`，未发现旁路。

### I1 commit point 后取消一致性：**Addressed**

- `LoomX/Assistant/Configuration/TomlDocumentService.cs:934-947` 只有原子 Replace/Move 失败时返回未提交失败；调用成功即越过 commit point。
- `LoomX/Assistant/Configuration/TomlDocumentService.cs:949-959` 提交后的目标验证和恢复使用 `CancellationToken.None`，不会再被原调用 token 中断。
- `TomlDocumentServiceTests.cs:1080-1145` 分别覆盖 Replace 后取消、Move 后取消、提交后验证失败且 token 已取消仍恢复原文件。
- 未发现“磁盘已提交但 API 抛取消”或“恢复被原 token 中断”的路径。

### I2 per-path lock 与 source fingerprint：**Addressed**

- `LoomX/Assistant/Configuration/TomlPathLockPool.cs:5-39` 使用静态池，跨 service 实例共享；`Path.GetFullPath` 加 `OrdinalIgnoreCase` 覆盖 Windows 大小写等价路径。
- `TomlPathLockPool.cs:17-37,41-63` 在等待前增加引用，取消等待时减少引用；只有引用归零且 entry 标记 Removed 后才移除并 Dispose semaphore。持有者/等待者存在时不会 Dispose，未发现释放后等待、双重释放或新旧 entry 误删竞态。
- `TomlDocumentService.cs:151-160` 从源读取前取得路径锁，锁覆盖读取、候选构造、备份、临时写入、验证和提交。
- `TomlDocumentService.cs:241-300` 保存源字节 SHA-256；`844-932` 在备份前和原子提交前两次比较 fingerprint；`1012-1037` 返回固定、无敏感内容的结构化冲突。
- `TomlDocumentServiceTests.cs:1148-1259` 覆盖跨 service 同路径串行、Windows 大小写等价、不同路径并行和外部修改冲突。
- fingerprint 检查与 OS Replace 之间仍有极短外部竞态；按本 Change 契约和报告已声明风险，不升级为 finding。若未来要求对任意外部进程严格 CAS，需平台文件锁/版本协议。

### I3 Broker 发布后无人 claim 收敛：**Addressed**

- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:120-150` 在同步事件发布返回后检查 `HasClaim`；无人 claim 时移除 pending 并以固定安全异常完成任务。
- `LoomX/ViewModels/AssistantViewModel.cs:337-368` 在事件回调内、调度 Dialog 前同步 `TryClaim`；inactive、busy、非当前 broker、多 VM 未获 claim 的实例只退出，不会取消他人请求。
- `AssistantViewModel.cs:306-335,388-514,1118-1134` 的 deactivate、submit、cancel、dispose 都以 owned request id 和 claimant id 收敛；提交/停用竞态不会留下 pending 或误抢。
- `UserDecisionBrokerTests.cs:211-324,391-570` 及 `AssistantViewModelTests.cs:434-581` 覆盖无订阅者、订阅者未 claim、旧 delegate、dispose/submit、submit/cancel、token 取消、多 VM 与 deactivate 竞态。
- 未发现发布后永久悬挂路径。

### I4 AskUser Schema 与运行时 optional 类型集合：**Addressed**

- `LoomX/Assistant/AssistantTools.cs:323-418` 对 optional string、boolean/integer/number、options array、string array 均区分“属性缺失”和“属性存在”；显式 null、错误 JsonValue 类型、对象/数组形态错误统一抛 `UserDecisionValidationException`。
- `AssistantTools.cs:446-498` 的 Schema 与解析器接受的 JSON 基本类型一致；handler 将校验异常统一映射为 `invalid_request`。
- `AssistantToolsTests.cs:377-451` 参数化覆盖 request、field、option 的全部 optional 属性错误类型，并覆盖显式 null。
- 未发现首轮所述“已知属性错误类型被静默当成未提供”的路径。

### M1 BOM 与 `formatting_changed`：**Addressed**

- `TomlDocumentService.cs:241-300` 读取并记录 UTF-8 BOM；`892-897` 使用与源一致的 UTF-8 encoding 写临时文件；新文件继续无 BOM。
- no-op 在 `218-222` 返回前不写文件；真实 set/delete/patch 会保留源 BOM。
- `TomlDocumentServiceTests.cs:1261-1300` 对 set、delete、patch 和 no-op 做字节级 BOM 验证，且 `FormattingChanged=false` 与当前支持的 UTF-8 边界一致。
- 未发现本次支持范围内仍会静默改变 BOM 的路径。

## 修复 diff 新增问题检查

除 C1 原 finding 尚未完全关闭外，未发现修复 diff 新引入的独立 Critical、Important 或 Minor。

## Standalone 发布产物复核

发布目录：`outputs/2026-09-17-1732-structured-config-assistant-decisions`

- `LoomX.exe` ProductVersion：`0.12.6+2f0f315c3f16fad706f278af0ea2887ceaadae1d`
- `LoomX.dll` ProductVersion：`0.12.6+2f0f315c3f16fad706f278af0ea2887ceaadae1d`
- `LoomX.exe` SHA-256：`EEC1F27266A11E6673ED4962810C8931A9FFB2EF80632F9A8BA00EEF1DC01C6B`
- `LoomX.dll` SHA-256：`0125513B880D2748CAED3345A8F692A72CB42001B162B42E7F93E0ADD36227D9`
- 文件时间为 2026-09-17 17:32，晚于生产提交 `2f0f315` 的 17:31，早于报告提交 `e624dfb` 的 17:37；`2f0f315..e624dfb` 仅修改修复报告与 Comet 进度文件，没有生产代码差异。
- 日志 `C:\Users\BianShanghai\AppData\Local\LoomX\logs\loomx-20260917.log` 中 PID `43340` 的多实例启动、实际 exe 路径和后续概览初始化各 1 条；路径精确指向本发布目录。
- 当前 PID `43340` 已不存在；既有 PID `28460` 仍存活且路径保持为另一工作区产物，符合隔离停止证据。

结论：发布产物与生产代码提交 `2f0f315` 的版本绑定、PID/Path/日志和停止隔离证据可信。

## 实际验证

- Scoped package：`git apply --reverse --check review-9681855..e624dfb.diff`，退出码 `0`，可从当前 Head 干净反向应用至指定基线。
- `git diff --check 9681855..e624dfb`：通过，无输出。
- `dotnet test LoomX.slnx --no-build --nologo`：**920/920 通过，0 失败，0 跳过**。
- 验证后 `git status --short --branch`：工作区与 index 干净；仅本报告随后按用户授权写入，未修改生产代码、测试、计划、OpenSpec、Comet 状态或 Git 元数据。

## 最终结论

**Changes requested**

**Ready to merge: No**

阻塞原因：首轮 C1 仍存在可复现的原始参数回流路径。其余 6 条 finding 已 addressed，发布证据可信。