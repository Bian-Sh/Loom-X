# Function Call 生命周期与 AnyRouter Codex 请求修复验证报告

## 结论

PASS。SenseNova 工具参数分片、重复快照与同 id 扇出已能稳定组装为单个合法 JSON object；AnyRouter `gpt-6-astra` 的 Codex/Responses 请求已对齐当前 Codex CLI 契约，不再以 400 `invalid_responses_request`（`invalid codex request`）拒绝首轮请求，并已完成一次低频真实 Function Call 全生命周期验收。

## 变更规模

- OpenSpec 任务：3/3 完成
- delta spec：0
- 生产代码：`OpenAiCompatibleModelClient.cs`、`OpenAiResponsesBridge.cs`、`CliIdentityService.cs`
- 测试代码：上述三个组件的对应测试文件
- Comet 状态与精简设计文档同步纳入当前 change
- 未修改数据库结构、公开 API 或生产日志敏感信息边界

## OpenSpec 完整验证记分卡

| 维度 | 状态 | 结果 |
| --- | --- | --- |
| 完整性 | PASS | 3/3 tasks 完成；无 delta spec，不存在未实现的规格要求 |
| 正确性 | PASS | proposal 的 4 项目标均有实现与测试证据，核心真实场景通过 |
| 一致性 | PASS | 实现遵循 design.md 的参数组装、Responses 桥接、工具名映射与低频验收设计 |

未发现 CRITICAL、WARNING 或 SUGGESTION 级验证问题，可以进入归档前确认。

## 根因消除证据

- 指定会话文件最终状态为 `Failed`；在失败前存在多轮 assistant tool call 与 tool result，证明问题位于调用参数/后续请求生命周期，而不是工具注册缺失。
- `loomx-20260910.log` 在 21:54:42 与 21:56:33 两次记录 `invalid_responses_request`，与请求列表中的 `invalid codex request` 对应。
- Codex 请求不再使用宽松 Chat 消息改名：system/developer 提升为 `instructions`，消息内容转换为 typed `input_text`/`output_text`，并使用 `store:false`。
- Codex 身份统一为 `codex_cli_rs`，请求带 `session-id`、`thread-id`、`x-client-request-id`、`prompt_cache_key` 与 `client_metadata`；同一生命周期保持稳定会话键，每轮使用独立 `turn_id`。
- Responses 工具名在线上规范化，模型返回后恢复 ToolRegistry 原名，避免 `loomx.*` 名称违反网关约束。
- 400 错误回归测试分别保留 `invalid_responses_request` 类型与 `invalid codex request` 消息，诊断数据不会互相覆盖。

## 验证证据

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| SenseNova 参数流解析 | PASS | 覆盖空 `finish_reason`、并行参数分片、同 id 扇出与重复完整快照 |
| 无效参数阻断 | PASS | 不完整 JSON 在模型客户端层安全失败，不进入工具执行与会话历史 |
| 离线生命周期 | PASS | 首轮 Function Call → AgentLoop 工具执行 → `function_call_output` → 第二轮最终回答 → Session Completed |
| Codex 请求契约 | PASS | 覆盖身份头、会话键、typed input、reasoning、工具 strict/choice 与 SSE Accept |
| AnyRouter 错误分类 | PASS | HTTP 400 的 error type 与 message 分别保留 |
| AnyRouter 真实验收 | PASS | `anyrouter/gpt-6-astra` 仅运行 1 个用例，最多 2 次请求、请求间隔至少 5 秒；1/1 通过，约 29 秒 |
| 全量测试 | PASS | 535/535 通过 |
| 定向格式检查 | PASS | 仅检查本任务 6 个 C# 文件，`dotnet format --verify-no-changes` 退出码为 0 |
| Release 构建 | PASS | 0 错误，6 个既有警告 |
| 桌面发布 | PASS | `outputs/20260911-005202/`，唯一应用入口为 `LoomX.exe` |
| 安全检查 | PASS | 无硬编码 API Key；真实测试只读统一数据库路径，不输出请求正文、响应正文、用户 prompt 或工具参数 |
| 代码审查 | PASS | 核对 Responses、CLI 身份、工具名映射和 Chat Completions 隔离边界；Comet `review_mode: off`，未派发额外审查 |

## 已知非阻塞项

- `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 的 NU1903 漏洞警告为仓库既有依赖问题。
- `SettingsViewModel` 的 CS8618、`AnthropicResponseMapper` 的 CA2024 与两处测试 CS8602 为既有警告，本次未改动对应文件。
- 全仓格式检查仍会受到既有编码、CRLF 与无关模块格式差异影响；本次仅对任务涉及的 6 个 C# 文件执行并通过定向检查，未批量改写无关文件。
- 现有 `graphify-out` 图谱仍包含项目更名前的 `OllamaHub` 路径，查询结果不适合作为本次正确性证据；实际代码关系已通过最新 CodeGraph 索引与源码核对。
- 当前终端未提供 `openspec` CLI，完整性、正确性与一致性检查直接读取 change 下的 proposal/design/tasks 完成；该 change 无 delta spec，因此没有跳过规格场景。

## 分支状态

- 分支：`master`
- 本报告对应本次实现变更；提交与推送结果以 Git 历史为准。
