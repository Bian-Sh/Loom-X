# 修复设计

在 `OpenAiCompatibleModelClient` 内收紧工具调用组装语义：每个逻辑调用只保留一份参数候选，兼容标准 append-only 增量，也兼容上游重复发送累计快照或完整副本。合并时优先保留能够形成有效 JSON 的最长一致候选，禁止把多个完整 JSON 快照拼成 `{}{}...`。流结束时只发射名称有效且参数为合法 JSON 的工具调用；不完整参数必须作为模型响应错误显式失败，不能进入工具执行与后续历史。

在 `OpenAiResponsesBridge` 内把 Chat 历史转换为严格的 Responses 请求：system/developer 内容提升为 `instructions`；user/assistant 字符串内容转换为带 `type: message` 的 `input_text`/`output_text` 内容块；纯工具调用的 assistant 消息只输出 `function_call` item；请求显式使用 `store:false`。当存在 reasoning effort 时补齐 Codex 的 summary 与连续性 include 字段。

在 `OpenAiCompatibleModelClient` 内为 Responses 请求建立本次调用专用的工具名映射：线上名称只保留字母、数字、`_`、`-` 且不超过 64 字符，模型返回后再恢复为 ToolRegistry 中的原名，保证 `loomx.*` 工具无需改名。检测到 Codex CLI 身份时，将历史 `codex-cli` 头兼容升级为当前 `codex_cli_rs`，补充稳定且一致的 `session-id`、`thread-id`、`x-client-request-id`，并在请求体加入 `prompt_cache_key`、`client_metadata`、`Accept: text/event-stream` 与缺省 low reasoning。每次模型请求使用独立 `turn_id`，同一 AgentLoop 生命周期保持稳定的 session、thread 与 root turn。该修复保持 Chat Completions 路径不变，也不依赖 Provider 名称硬编码。

测试分三层：

1. 解析回归测试复现 SenseNova 的参数分片、同 id 扇出和重复快照，断言最终参数是单个合法 JSON。
2. 生命周期测试使用真实 `OpenAiCompatibleModelClient` 和 `AgentLoop`，由脚本化 HTTP 响应驱动首次工具调用与第二次最终回答，断言工具只执行一次、第二次请求携带成对的 assistant/tool 消息、会话最终完成。
3. 显式启用的本机 SenseNova 验收测试从 `%LOCALAPPDATA%\LoomX\LoomX.db` 只读加载 Provider、模型和 DPAPI 密钥，使用一个无副作用的测试工具完成一次真实生命周期。该测试默认直接返回，不进入常规测试流；启用后限制最大步骤和总超时，并只运行一个用例，避免高频请求。
4. 同一真实验收入口允许显式选择 AnyRouter `gpt-6-astra`，每个生命周期最多两次请求、请求间隔至少 5 秒；先以单次失败复现确认 RED，修复后只复跑一次完整生命周期。

不修改数据库结构、公开 API、工具协议或生产日志的敏感信息边界。
