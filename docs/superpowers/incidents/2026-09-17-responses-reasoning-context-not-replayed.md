# Responses thinking 上下文未续传导致 HTTP 400

## 状态

- 类型：待处理的独立协议兼容性问题
- 优先级：高
- 当前只记录证据与调查方向，不纳入 `fix-browser-bridge-connectivity` 的实现范围。
- 本任务适用当前日期为 2026-09-17；运行环境日志的机器时钟显示为 2026-09-18，以下保留原始时间戳用于定位，二者存在日期偏差。

## 用户可见错误

```text
HTTP 状态码：400
错误码：invalid_request_error
服务描述：The `reasoning_text` in the thinking mode must be passed back to the API. [trace_id=***]
```

## 复现上下文

- Assistant Session：`b510495d918f4d7e934ba97eb8099a6f`
- Provider：`AgentRouter`
- 模型：`deepseek-v4-flash`
- Assistant 权限模式：自动批准
- 思考等级：UI 显示“默认”
- API 路径：OpenAI Responses 兼容路径
- 任务类型：多步骤 Agent 工具循环，包含 `browser.bridge_start`、`skill.load` 等工具调用。
- 前 5 个模型步骤正常完成；第 6 步在携带先前 Assistant/Tool 历史继续请求时被上游以 HTTP 400 拒绝。
- 机器日志时间戳：2026-09-18 01:11:43 选择 `AgentRouter/deepseek-v4-flash`，2026-09-18 01:11:56 开始步骤 6，2026-09-18 01:11:57 返回 400。

## 初步判断

### 已确认的代码事实

1. `AgentLoop` 会把 `ReasoningDeltaEvent` 保存为 `ChatMessage.Blocks` 中的 `ChatContentKind.Thinking`。
2. `OpenAiCompatibleModelClient.BuildPayload` 重新构造历史请求时只读取 `ChatMessage.Content`、`ToolCalls` 和 Tool 结果，没有读取 `ChatMessage.Blocks` 中的 Thinking 数据。
3. `OpenAiResponsesBridge.ConvertInput` 只转换 message、function_call 和 function_call_output，也没有重建 Responses reasoning item、`reasoning_text` 或 `reasoning.encrypted_content`。
4. 因此多步骤工具循环进入下一轮时，前一轮的 reasoning 上下文可能已经在 LoomX 内存/Session 中存在，但不会按上游 Responses thinking 协议原样回传。

### 关于“默认”的假设

“默认”是**待验证诱因，不是已确认根因**：

- LoomX 的 `default` 表示不显式下发 `reasoning_effort`。
- `deepseek-v4-flash` 或 AgentRouter 可能在未指定时默认开启 thinking，并要求后续请求回传其 `reasoning_text`。
- 显式选择 `none/minimal/low` 等等级可能改变上游 thinking 行为，从而绕开或改变错误，但即使能绕开，也不能替代 LoomX 对 Responses reasoning 上下文的正确建模与续传。

当前更可能的核心缺口是：**LoomX 把 reasoning 当作 UI 可显示文本块，而不是需要跨 Responses 工具轮次保真的协议状态。**

## 后续复现矩阵

对同一 Provider/模型、同一两步工具调用脚本分别验证：

| 思考设置 | 预期检查 |
|---|---|
| 默认 | 是否稳定在第二次或后续 Responses 请求返回 `reasoning_text must be passed back` |
| minimal | 是否仍进入 thinking；后续请求是否 400 |
| low | 是否仍进入 thinking；后续请求是否 400 |
| medium/high | 是否返回 reasoning item/encrypted content；后续请求是否 400 |
| none（上游支持时） | 是否禁用 thinking 并正常完成工具循环 |

同时对比：

- 无工具的单轮请求；
- 一个工具调用后继续；
- 两个以上连续工具调用；
- Chat Completions 与 Responses 两种 endpoint format。

## 修复方向

1. 扩展模型事件与消息结构，保留 Responses reasoning item 所需的稳定标识、文本/摘要及 `encrypted_content`，不要只保存展示文本。
2. `OpenAiResponsesBridge` 在下一轮 input 中按原始顺序回放 reasoning item、function_call 和 function_call_output。
3. 若上游只要求 `previous_response_id`，评估以 response chaining 代替完整重放；不得在未确认 Provider 兼容性前假设所有 Responses 实现一致。
4. 在协议能力未修复前，可考虑对已知要求 reasoning 回传的模型提供安全降级：显式禁用 thinking，或在 UI 中阻止“默认”进入不受支持模式；降级必须可见，不能静默改变用户选择。
5. 新增回归测试，构造“reasoning → function_call → function_call_output → 下一轮请求”，断言第二轮包含上游要求的 reasoning 状态。
6. 日志只记录 Provider、Model、endpoint format、reasoning 设置、步骤和状态码，不记录 reasoning 正文、prompt、工具参数或响应正文。

## 验收草案

- `AgentRouter/deepseek-v4-flash` 在默认及明确 reasoning 设置下完成至少 3 轮工具循环，不再返回该 400。
- Session 恢复后继续工具循环时，协议状态仍完整。
- reasoning 原文或加密内容不得泄漏到普通日志、Toast 或错误摘要。
- 若 Provider 不支持所选 reasoning 设置，LoomX 在发送前给出明确兼容性提示，而不是由上游在后续步骤 400。
