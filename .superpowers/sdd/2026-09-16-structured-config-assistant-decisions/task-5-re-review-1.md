# Task 5 修复轮 1 复审报告

## Finding Verdicts

1. **AskUser 敏感边界未覆盖提交结果和常见自由文本秘密变体** — **ADDRESSED**。`SensitiveKeyPolicy.ContainsSensitiveContent` 已将内容级检测与 TOML 路径检测分离，并覆盖敏感名称变体、Authorization/Bearer、常见 key/token 前缀与 JWT（`LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:25-29`、`LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:31-93`、`LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:136-150`）。请求标题、问题、说明、影响摘要、字段标题、选项标签/说明及文本默认值统一进入该检测（`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:208-222`、`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:275-281`、`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:434-456`、`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:493-522`），Text 提交值也在结果构造和移除 pending 前被拒绝（`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:630-655`、`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:148-179`）。校验错误只使用字段 id 与固定安全消息，Broker 只记录计数/请求 id；敏感值不会进入成功结果。原 `IsSensitivePath`/`Redact` 调用链保持独立，TOML 路径脱敏未被替换（`LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:95-110`、`LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:152-175`）。变体、展示文本、敏感提交及 TOML 回归测试证据见 `LoomX.Tests/Assistant/SensitiveKeyPolicyTests.cs:8-96`、`LoomX.Tests/Assistant/UserDecisionModelsTests.cs:128-186`、`LoomX.Tests/Assistant/UserDecisionBrokerTests.cs:305-345`。

2. **四类字段未形成封闭判别契约** — **ADDRESSED**。字段类型通过 enum 的 `JsonStringEnumMemberName` 固定为 `single_select`、`multi_select`、`number`、`text`（`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:7-21`）。四个类型分支分别调用专属属性封闭校验，选择、数值、文本属性集合均被完整归类并逐类型拒绝不适用属性（`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:283-300`、`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:303-432`、`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:467-523`）。四类稳定 JSON 往返和跨类型非法组合测试已锁定契约（`LoomX.Tests/Assistant/UserDecisionModelsTests.cs:234-295`）。

3. **Submit 重复枚举且移除后仍可能访问调用方对象** — **ADDRESSED**。`Submit` 在移除 pending 前调用 `CreateSubmissionSnapshot`，仅顺序枚举调用方字典一次，并把 multi `IEnumerable<string>` 一次性复制为受控只读集合；校验与 `UserDecisionResult` 均只消费该快照（`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:138-179`、`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:262-284`）。快照或校验失败直接返回且不移除 pending；`TryRemove` 成功后只完成已构造结果，不再访问调用方对象，并通过 `finally` 释放 registration（`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:148-179`、`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:286-295`、`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:333-370`）。单次枚举、快照失败重试、Submit/Cancel 与 Submit/token 并发测试覆盖了原缺陷和竞态收敛（`LoomX.Tests/Assistant/UserDecisionBrokerTests.cs:284-323`、`LoomX.Tests/Assistant/UserDecisionBrokerTests.cs:347-413`）。

4. **无订阅者/Broker dispose 无有界收敛** — **ADDRESSED**。接口已继承 `IDisposable`（`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:7-21`）；请求在生命周期锁内检查 disposed 与订阅者，无订阅者时不创建 pending 并立即返回固定安全异常（`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:72-98`）。`Dispose` 在同一生命周期锁内封闭新请求、清空订阅并移除全部 pending，随后取消任务并释放 registration；Submit、token 取消和 dispose 仍以 `TryRemove` 决胜，因此并发完成有界（`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:232-259`、`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:286-315`）。无订阅者、释放全部 pending、dispose 后请求和 dispose/Submit 并发测试均带超时或确定性终态断言（`LoomX.Tests/Assistant/UserDecisionBrokerTests.cs:201-282`）。

5. **OwnerId 日志泄漏** — **ADDRESSED**。所有日志模板及参数仅保留生成的 request id、字段/错误/请求计数和异常类型，不再传入 `ownerId`、values、reason 或自由文本；事件订阅者异常也改为固定安全异常对象，原异常仅取类型名（`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:93-131`、`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:153-183`、`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:187-229`、`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:232-258`、`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:300-310`）。测试同时检查格式化消息、logger state 和异常文本不含原始 ownerId，并覆盖提交值/取消原因不进入日志（`LoomX.Tests/Assistant/UserDecisionBrokerTests.cs:415-459`）。

## New Breakage in the Fix Diff

None。未发现修复 diff 新增的 Critical、Important 或 Minor breakage。

## Out-of-Scope Observations

None。协调者已明确将原审查 Important 6（OpenSpec tasks 勾选）留待复审通过后处理，原 Minor 文件拆分也不阻断；本报告不将二者重新登记为 open finding。

## Verdict

**Fix round:** All findings addressed, no new Critical/Important breakage。