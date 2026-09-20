# AskUser 取消生命周期修复验证报告

## 1. 反馈与根因

用户点击 AskUser 卡片右上角关闭按钮时，UI、ViewModel 与 Broker 的取消链路实际已经完成：`AskUserDialogViewModel.TryCancel()` 将卡片 `Completion` 完成为 `false`，`AssistantViewModel` 调用 `UserDecisionBroker.Cancel()`，工具结果为：

```json
{"cancelled":true,"values":{},"custom_inputs":{}}
```

缺陷位于 `AgentLoop`。取消结果被当作普通成功工具结果后，下一次模型请求仍公开 `assistant.ask_user`；模型重复调用时会创建新的 Pending 请求并重新展示卡片。若用户随后完成第二张卡片，助手最终看到的是第二次提交的 `cancelled=false`，从而产生“面板未取消”的错误汇总。

## 2. 修复行为

单次 `AgentLoop.RunAsync` 现在维护本轮已终止工具结果：

1. 首次 AskUser 返回 `cancelled=true` 后，保存原结构化取消结果；
2. 本轮后续 `ModelRequest.Tools` 移除 `assistant.ask_user`；
3. 即使模型仍生成重复 AskUser 调用，也不再执行 Handler，不进入 Broker/UI，而是复用原 `cancelled=true`；
4. AgentLoop 不直接终止整个助手轮次，模型仍可根据取消结果生成准确摘要；
5. 新用户轮次重新创建状态，AskUser 可正常再次使用。

## 3. TDD 证据

新增回归测试：

```text
AgentLoopTests.AskUser返回取消结果_应继续总结且不再展示AskUser
```

RED 阶段失败证据：

```text
Expected: Completed
Actual:   Cancelled
```

GREEN 阶段断言：

- 模型在收到取消结果后再次生成 AskUser 调用；
- AskUser Handler 总调用次数仍为 1；
- 后续模型请求不再公开 `assistant.ask_user`；
- 两条工具结果均保持 `cancelled=true`；
- AgentLoop 最终完成，并输出“面板已取消（cancelled: true）”。

## 4. 自动化验证

### AskUser 与 AgentLoop 定向测试

```text
dotnet test LoomX.Tests\LoomX.Tests.csproj -c Release --no-restore \
  --filter "FullyQualifiedName~UserDecision|FullyQualifiedName~AskUser|FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AgentLoopTests"

已通过：213 / 213
```

### 完整测试

```text
dotnet test LoomX.Tests\LoomX.Tests.csproj -c Release --no-restore

已通过：1113 / 1113
```

### Release 构建

```text
dotnet build LoomX.slnx -c Release --no-restore

结果：0 error，2 个既有 NU1903 warning
```

### OpenSpec

```text
openspec validate enhance-ask-user-custom-input --strict

Change 'enhance-ask-user-custom-input' is valid
```

## 5. 发布包

```text
outputs/2026-09-21-064653-ask-user-cancel-lifecycle/LoomX.exe
```

发布成功并确认 `LoomX.exe` 存在。尝试启动新包进行桌面复验时，LoomX 单实例机制检测到旧发布包 `outputs/2026-09-20-043915-ask-user-custom-input/LoomX.exe` 已运行，因此新进程立即退出。为避免关闭用户正在使用的实例，本轮未强制终止旧进程。取消重弹属于 AgentLoop 生命周期问题，已由覆盖重复模型调用、Handler 次数、工具可见性、工具结果和最终摘要的回归测试确定性验证。

## 6. 审查结论

- UI 关闭按钮与 Broker 取消链路无需修改，避免在表现层重复打补丁。
- 禁用状态仅限当前 `RunAsync`，不会永久关闭 AskUser。
- 重复调用不读取或记录工具参数、用户 prompt 或输入内容。
- 日志只记录工具名，不包含用户输入或取消原因。
- 未修改数据库、持久化格式或其他 Session 产物。

本轮未发现新的 CRITICAL 或 WARNING 级问题；既有 NuGet 漏洞告警未由本次变更新增。
