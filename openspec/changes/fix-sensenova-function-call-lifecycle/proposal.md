# 修复 SenseNova Function Call 生命周期

## Why

真实会话 `e3f54c0fc409467d82a300f45bbd9171` 中，SenseNova `glm-5.2` 已经成功返回多组 `loomx.test_endpoint` 工具调用，但会话文件里的参数被保存为单个 `{` 或空串。工具执行随后连续返回“工具参数不是有效的 JSON”或“缺少必填参数 'key'”，第 4 次模型请求最终被上游以 400 `InvalidRequest` 拒绝，用户看到的表现是 tool call 无法继续。

同一会话切换到 AnyRouter `gpt-6-astra` 后，首轮请求又被 `/v1/responses` 以 400 `invalid_responses_request`（`invalid codex request`）同步拒绝。该 Provider 已启用 Codex CLI 身份头，但 LoomX 仍把 Chat Completions 的字符串消息直接改名为 `input`，没有生成 Codex/Responses 要求的 `instructions`、typed message content 与 `store:false` 请求形态。

## 问题

SenseNova 在工具参数增量帧中使用 `finish_reason:""`，只在最后一帧返回 `tool_calls`。当前 Chat Completions SSE 解析器把任意非 null 的 `finish_reason` 都当成完成信号，因此会在首个 `{` 后提前排空参数累积器；后续不带 id/name 的参数片段随即被当成孤立占位丢弃。与此同时，同 id 扇出副本的 `function.arguments` 又会被无条件追加，现有回归测试甚至把 `{}{}...` 这种无效 JSON 当作正确结果。AgentLoop 最终执行并持久化这些无效调用，进一步触发上游协议错误。

AnyRouter 场景中，请求桥接只完成了字段改名与工具扁平化，没有完成 Chat 消息到严格 Responses input item 的转换。系统消息仍留在 `input`，用户消息仍是字符串 `content`，因此携带 Codex CLI 身份的请求在进入模型前即被网关判定为无效 Codex 请求。

## 根因

解析层错误地把空字符串 `finish_reason` 视为完成状态，同时工具参数组装缺少“最终结果必须是单个有效 JSON object”的不变量。去重只覆盖调用数量，没有覆盖参数完整性；重复快照与真实增量都被视为可无条件追加的字符串，导致不完整或重复 JSON 进入 AgentLoop。

AnyRouter 根因是 LoomX 只做了宽松 Responses 字段改名，却同时发送包含 `.` 的工具名、旧式 `codex-cli` 身份头并缺少 Codex 会话头。该组合不符合当前 Codex CLI 的严格请求契约，因此 AnyRouter 在首轮模型执行前直接返回 `invalid codex request`。

## 目标

1. SenseNova 风格的分片、重复快照和扇出工具调用最终只产生唯一且参数完整的 ToolCall。
2. 自动测试覆盖模型流、工具执行、工具结果回传、第二轮模型回答和会话完成的完整生命周期。
3. 提供显式开启、默认不访问真实用户配置的 SenseNova 低频验收测试；测试代码不硬编码 API Key，也不输出请求正文、响应正文或工具参数。
4. AnyRouter/Codex Responses 首轮请求使用合法的 `instructions`、typed input items 与无状态模式，并能继续完成同一套 Function Call 生命周期验收。
