# AskUser 取消生命周期修复验证报告

## 1. 反馈与根因

用户点击 AskUser 卡片右上角关闭按钮时，UI、ViewModel 与 Broker 的取消链路实际已经完成：`AskUserDialogViewModel.TryCancel()` 将卡片 `Completion` 完成为 `false`，`AssistantViewModel` 调用 `UserDecisionBroker.Cancel()`，工具结果为：

```json
{"cancelled":true,"values":{},"custom_inputs":{}}
```

首个缺陷位于 `AgentLoop`。取消结果被当作普通成功工具结果后，下一次模型请求仍公开 `assistant.ask_user`；模型重复调用时会创建新的 Pending 请求并重新展示卡片。若用户随后完成第二张卡片，助手最终看到的是第二次提交的 `cancelled=false`，从而产生“面板未取消”的错误汇总。

独立审查随后发现 Responses/Codex 路径的关联回归：从当前工具列表移除 AskUser 后，`OpenAiCompatibleModelClient` 同时失去了历史 `assistant.ask_user` 的 wire name 映射，下一轮历史 `function_call.name` 会从 `assistant_ask_user` 退化为带点名称，重复调用也无法还原为逻辑工具名。

## 2. 修复行为

单次 `AgentLoop.RunAsync` 现在维护本轮已终止工具结果：

1. 首次 AskUser 返回 `cancelled=true` 后，保存原结构化取消结果；
2. 本轮后续 `ModelRequest.Tools` 移除 `assistant.ask_user`；
3. 即使模型仍生成重复 AskUser 调用，也不再执行 Handler，不进入 Broker/UI，而是复用原 `cancelled=true`；
4. AgentLoop 不直接终止整个助手轮次，模型仍可根据取消结果生成准确摘要；
5. 新用户轮次重新创建状态，AskUser 可正常再次使用。

Responses 工具名映射现在同时收集当前可用工具与历史 assistant tool call，并按逻辑工具名稳定排序后生成规范化名称。AskUser 虽然不再向模型公开，但历史调用仍序列化为 `assistant_ask_user`，模型返回的同名重复调用仍能还原为 `assistant.ask_user`，从而命中本轮取消结果复用。

## 3. TDD 证据

新增回归测试：

```text
AgentLoopTests.AskUser返回取消结果_应继续总结且不再展示AskUser
OpenAiCompatibleModelClientTests.StreamAsync_Responses禁用工具后仍保留历史工具名映射
```

RED 阶段分别确认：

- 取消后 AgentLoop 会被错误终止或再次执行 AskUser；
- Responses 请求中的历史调用名实际为 `assistant.ask_user`，而不是规范化的 `assistant_ask_user`。

GREEN 阶段断言：

- AskUser Handler 总调用次数仍为 1；
- 后续模型请求不再公开 `assistant.ask_user`；
- 两条工具结果均保持 `cancelled=true`；
- AgentLoop 最终完成，并输出取消摘要；
- Responses 请求不公开已禁用工具，但历史 `function_call` 继续使用规范化名称；
- Responses 返回的 `assistant_ask_user` 能还原为 `assistant.ask_user`。

## 4. 自动化验证

### Responses 与 AgentLoop 定向测试

```text
dotnet test LoomX.Tests\LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~OpenAiCompatibleModelClientTests"
已通过：41 / 41

dotnet test LoomX.Tests\LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AgentLoopTests"
已通过：25 / 25
```

### 完整测试

```text
dotnet test LoomX.Tests\LoomX.Tests.csproj -c Release --no-restore

已通过：1114 / 1114
Comet 证据：openspec/changes/enhance-ask-user-custom-input/.comet/checks/9b342f01-83d1-4a2b-b21c-5cd81a1d3ac4.log
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
outputs/2026-09-20-071229-ask-user-cancel-lifecycle-r2/LoomX.exe
```

发布成功并确认 `LoomX.exe` 存在。桌面复验未强制启动新包，因为当前已有两个 LoomX 实例运行，其中包括旧发布包 `outputs/2026-09-20-043915-ask-user-custom-input/LoomX.exe`。为避免关闭用户正在使用的实例，本轮未终止任何进程。取消重弹与 Responses 映射均已由确定性回归测试覆盖。

## 6. 审查结论

- UI 关闭按钮与 Broker 取消链路无需修改，避免在表现层重复打补丁。
- 禁用状态仅限当前 `RunAsync`，不会永久关闭 AskUser。
- Responses 历史工具名与返回工具名保持稳定映射，不会因隐藏当前工具而退化。
- 重复调用不读取或记录工具参数、用户 prompt 或输入内容。
- 日志只记录工具名，不包含用户输入或取消原因。
- 未修改数据库、持久化格式或其他 Session 产物。
