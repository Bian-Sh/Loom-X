# Structured Config & Assistant Decisions 整分支最终审查

- 审查日期：2026-09-17
- 分支：`codex/structured-config-assistant-decisions`
- 范围：`a9e755d2ff2e924c6b23a589a027d8f8bca64a2d..94e1f2cf1d047200493339b0b1c706c8d07bcd26`
- 规模：90 个文件，约 14,908 行新增、34 行删除
- 结论：**Ready to merge: No**

本审查以实际代码为准，分轮检查了 TOML 语法树读写、事务与回滚、敏感投影、Assistant 工具链、会话持久化、AskUser Broker/owner/UI 生命周期、系统提示恢复、日志/Toast、本地化、测试和 standalone 交付证据。Review package 已通过反向应用校验，与指定 Head 一致。由于任务规定除本报告外不得写入任何文件，本轮未重新执行可能生成 `bin/`、`obj/` 或 TestResults 的 build/test；测试结论仅来自代码审阅、已提交测试及现有发布产物核验。

## Strengths

- TOML 实现基于 Tomlyn syntax tree 做局部编辑，而不是将整份文档 round-trip 为普通字典；`string[]` 路径能区分 dotted key 与名称中包含点号的 quoted key。
- set/delete/patch 对普通表、inline table、数组和 AoT 冲突设置了明确边界；批量 patch 先在内存候选上完成，避免半批次写入。
- no-op 在备份和临时写入之前返回；备份与临时文件位于目标同目录；Windows 已使用 `File.Replace`，并实现有限重试、临时文件验证、目标写后验证及多数失败路径的备份恢复。
- TOML 日志统一使用 `ILogger<T>`，错误通过安全异常和文件名摘要记录；本 Change 未新增 `Console.WriteLine`、`Console.Error.WriteLine` 或 `Debug.WriteLine` 业务诊断。
- AskUser 领域模型为不可变快照，提交时进行二次验证；Broker 的 claim/complete 操作本身有原子保护，TCS 使用 `RunContinuationsAsynchronously`；owner 通过每次异步枚举 `MoveNextAsync` 范围重建，人工等待工具也没有套用默认 30 秒超时。
- 用户提交的 Text 原文不会进入 ToolResult；ToolResult 只返回 `provided` 状态，正常日志和 Toast 不回显用户自由文本或取消原因。
- Avalonia Dialog 覆盖 single-select、multi-select、number、text 四类字段，含本地化校验摘要；`MainWindow`/`AssistantView` 已接入激活、停用和 Dispose 生命周期。
- 新会话和历史会话恢复后，实际 `ModelRequest` 均应用当前唯一 System Prompt；旧 System 消息会先被移除。
- System Prompt 与两个 Client Skill 明确了“原生/官方能力 → browser.open/read/wait → assistant.ask_user”的顺序，以及登录、CAPTCHA、Cloudflare、JS challenge 立即交还用户且不得绕过的边界；本 Change 未引入搜索 Provider/Secret、爬虫、Cookie 注入或 TLS/指纹/挑战绕过实现。
- 配置数据库路径逻辑未被本 Change 改动，仍由既有 `AppDataPaths` 统一指向 `%LOCALAPPDATA%\LoomX\LoomX.db` 和 `%LOCALAPPDATA%\LoomX\LoomX.Activity.db`。
- `outputs/2026-09-17-1601-structured-config-assistant-decisions` 中 `LoomX.exe`/`LoomX.dll` 的 ProductVersion 指向最后一个生产代码提交 `164b1e94...`；该提交到 Head 之间仅有审查/计划/OpenSpec/Comet 文档变更。发布包内两个本 Change 修改的 Client Skill 与当前源码 SHA-256 一致。

## Issues

### Critical

#### C1. 原始工具参数在安全校验和脱敏之前进入 Session、UI、持久化及后续模型请求

**位置**

- `LoomX.Harness/AgentLoop.cs:158-162`：工具执行前就把完整 `ToolCall`（含原始 `ArgumentsJson`）加入 Assistant 消息。
- `LoomX.Harness/AgentLoop.cs:179-195`：写/删工具批准前直接从原始参数生成审批详情。
- `LoomX/Assistant/AssistantSessionStore.cs:107-138`：`tool_calls` 和 `blocks.tool_call` 两处均原样序列化 `ArgumentsJson`。
- `LoomX/ViewModels/AssistantViewModel.cs:1621-1638`：工具调用 ViewModel 原样保存并展示参数。
- `LoomX/Assistant/AssistantService.cs:204-220`：审批 UI 使用原始参数的前 300 字摘要。
- `LoomX/Assistant/OpenAiCompatibleModelClient.cs:657-670`：下一轮模型请求再次原样发送 Session 中的工具参数。

**触发路径**

模型产生以下调用即可触发，且无论用户随后是否拒绝审批、TOML Handler 是否失败、AskUser 内容边界是否拒绝，请求原文都已先进入 Session：

```json
{
  "path": "C:\\Users\\Alice\\.codex\\config.toml",
  "key_path": ["provider", "headers", "X-Custom"],
  "value": "private-header-value"
}
```

`assistant.ask_user` 也存在同一路径：`AssistantTools.ParseRequest` 的敏感内容检查发生在 Handler 内，但 `AgentLoop` 已先保存模型生成的 title/question/default_text/Header/TOML/JSON 正文。

**影响**

- 直接违反“Secret、完整用户路径、自定义 Header 值、AskUser 自由文本不得进入 Session”的边界。
- 原文会进入内存会话、工具详情 UI、审批 UI，并在下一步 `ModelRequest` 中再次发送给上游模型。
- `AssistantSessionStore.SecretLeakScan`（`AssistantSessionStore.cs:401-406`）只兜底识别较长 `sk-` 和 `Bearer` 形态；普通 secret、自定义 Header 值和完整用户路径仍会持久化。即使扫描命中并拒绝落盘，也无法撤销已经发生的内存/UI/下一轮模型暴露。
- 现有 `TomlToolsTests.cs:347-370` 只断言 ToolResult 和 Agent 日志不含输入 secret，没有检查 Assistant tool-call 消息、jsonl、UI projection 或下一轮真实模型请求，因此测试可绿而生产边界仍失守。

**建议**

为 `ToolDefinition` 增加工具专用的安全参数投影/敏感参数元数据。Handler 执行时可短暂使用原始参数，但写入 Session、事件、审批、UI、持久化以及后续模型请求时必须替换为安全投影。TOML 工具只保留操作名、键路径安全摘要及文件名/不可逆路径摘要，绝不保留 value 或完整路径；AskUser 在进入 Session 前完成内容边界校验或仅保存字段结构摘要。补充覆盖 Session、jsonl、UI、审批和下一轮真实 `ModelRequest` 的回归测试。

#### C2. `toml.get` 只按键名脱敏，任意自定义 Header 或秘密字符串可直接进入 ToolResult

**位置**

- `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:14-23`：敏感名称集合不包含 `headers`、`custom_headers`、`http_headers` 等容器。
- `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:95-110`：非敏感路径的 string scalar 原样返回。
- `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:161-175`：对象递归只检查 property path；已实现的 `ContainsSensitiveContent` 没有用于 TOML scalar 投影。
- `LoomX/Assistant/TomlTools.cs:481-505`：未命中敏感路径的字符串被原样写入 JSON ToolResult。

**触发路径**

```toml
[provider.headers]
X-Custom = "Bearer abcdefghijklmnopqrstuvwxyz"
```

调用 `toml.get`，路径为 `['provider', 'headers']` 或 `['provider', 'headers', 'X-Custom']`。`headers` 与 `X-Custom` 均不命中敏感名称，Bearer 值会原样进入 ToolResult。普通键下的 `sk-...`、JWT 或其他 Secret 同样会泄漏。

**影响**

- 直接违反“自定义 Header 值、Secret 不得进入 ToolResult/Session”和 OpenSpec“原始敏感值不进入助手上下文”的要求。
- 该路径会把原本只存在于本地 TOML 文件中的凭据主动暴露给模型，风险高于单纯日志泄漏。
- 当前测试只覆盖 `api_key`/`token` 等敏感键名以及普通非敏感标量，没有覆盖任意 Header 名、父对象中的 Header 容器或非敏感键下的秘密值。

**建议**

把 `headers`、`custom_headers`、`http_headers` 等容器定义为“所有子值敏感”；同时对所有 string scalar 执行内容级检测，命中 Bearer/JWT/API key/token 形态时统一返回占位符。数组、inline table、普通 table、AoT 父对象读取均应复用同一策略，并增加 arbitrary header name、Bearer/JWT、benign-key-secret-value 的回归测试。

### Important

#### I1. 原子替换完成后仍使用可取消 token 做验证，取消可留下已修改文件却向调用方抛出取消

**位置**

- `LoomX/Assistant/Configuration/TomlDocumentService.cs:915-940`：`TryAtomicReplaceAsync` 成功后继续使用调用方 token 执行目标验证和恢复。
- `LoomX/Assistant/Configuration/TomlDocumentService.cs:993-1005`：恢复入口先 `ThrowIfCancellationRequested()`，恢复后验证也继续使用同一 token。
- `LoomX/Assistant/Configuration/TomlDocumentService.cs:1113-1119`：`ParseDocumentAsync` 一进入即响应取消。

**触发路径**

让文件操作实现先成功执行真实 `Replace`/`Move`，随后立即取消 token。`WriteCandidateAsync` 在 `ParseDocumentAsync(path, token)` 抛出 `OperationCanceledException`；`finally` 只清理 temp，不恢复目标。最终 API 报告取消，但磁盘已经是新内容。创建新文件时还可能留下一个调用方认为未完成的新目标。

**影响**

取消结果与磁盘状态不一致，违反“写后验证”和“任意失败回滚”的事务承诺。调用者可能重试同一 patch，或在错误假设下继续操作。

**建议**

定义清晰 commit point：替换成功后使用 `CancellationToken.None` 完成目标验证和必要恢复；若必须传播取消，也应先恢复到原状态再传播。增加“Replace 成功后取消”和“恢复阶段取消”的确定性测试。

#### I2. Patch 没有路径级串行化或源版本校验，会静默覆盖并发更新

**位置**

- `LoomX/Assistant/Configuration/TomlDocumentService.cs:164-221`：读取源内容、构建候选和进入写事务之间没有锁或版本检查。
- `LoomX/Assistant/Configuration/TomlDocumentService.cs:842-855`：写阶段只重新检查 `File.Exists` 并复制当前目标为备份，不确认当前内容仍等于生成候选时读取的 source。
- `LoomX/Assistant/Configuration/TomlDocumentService.cs` 全类没有 per-path `SemaphoreSlim`、mtime/hash/fingerprint compare-and-swap。

**触发路径**

1. Patch A 读取版本 A 并生成候选 A′。
2. 外部编辑器或另一调用把文件更新为版本 B。
3. Patch A 在写阶段备份 B，然后用基于旧版本 A 的 A′ 替换目标。
4. Patch A 返回 success，版本 B 的更新被静默丢失。

**影响**

产生 lost update。备份 B 虽然存在，但成功返回值错误地表示本次变更已在最新文件上正确合并；用户通常不会在“成功”后主动检查备份。

**建议**

对规范化绝对路径（Windows 下大小写不敏感，并处理等价路径）增加 per-path async lock；替换前重新读取并比较源 fingerprint/hash，若文件已变化则返回结构化冲突而不是覆盖。增加两个并发 Patch 以及“Read 后、Backup 前外部修改”的确定性测试。

#### I3. Broker 只在事件快照时判断“有订阅者”，事件未被 Claim 时会留下无限等待请求

**位置**

- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:76-95`：锁内捕获 delegate 并创建 pending，只要快照非 null 就认为存在处理器。
- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:120-139`：锁外调用快照后直接返回 completion task，没有确认请求是否已被 Claim。
- `LoomX/ViewModels/AssistantViewModel.cs:306-330`：Deactivate 只取消已经记录到 `ownedUserDecisionRequestId` 的请求。
- `LoomX/ViewModels/AssistantViewModel.cs:345-358`：旧事件到达 inactive/busy VM 时直接 return，不 claim、不 cancel、也不 release。
- `LoomX/Assistant/AssistantTools.cs:37-50`：`assistant.ask_user` 使用无限超时等待 Broker。

**触发路径**

Broker 在线程 A 捕获 UI handler 并创建 pending；线程 B 在 invoke 前 Deactivate/解除订阅，此时 VM 尚未 claim，所以没有 owned request 可取消；线程 A 随后调用旧 delegate，VM 因 inactive 直接 return。Broker 不检查 claim 状态，也不重放事件；AskUser 因无限 timeout 永久等待。即使没有竞态，只挂接一个不 claim 的订阅者也会得到同样结果。

**影响**

Assistant run 可永久悬挂，重新打开页面也收不到该 pending；用户只能取消整个 run。现有测试覆盖了真正无订阅者、Dialog 已打开后的 Deactivate、多个 VM 抢占和 submit/deactivate 竞态，但没有覆盖“snapshot → unsubscribe/inactive → invoke”或“发布后无人 claim”。

**建议**

Broker 发布后必须原子检查是否至少有一个 claimant；无人 claim 时立即移除 pending 并返回安全失败。或者让 inactive/busy handler 对旧事件执行 claim+cancel 的收敛协议，并为事件重放/新订阅者接管定义明确语义。增加带同步屏障的确定性竞态测试。

#### I4. AskUser JSON Schema 与运行时解析不封闭，错误类型的数组属性会被静默当成“未提供”

**位置**

- `LoomX/Assistant/AssistantTools.cs:106-107`：`options` 不是 `JsonArray` 时被归一为空数组。
- `LoomX/Assistant/AssistantTools.cs:288-294`：`default_option_ids` 不是 `JsonArray` 时也被归一为空数组。
- `LoomX/Assistant/AssistantTools.cs:344-358`：公开 Schema 明确要求上述属性为 array。
- `LoomX/Assistant/OpenAiCompatibleModelClient.cs:782-787`：发送给上游的 function tool 为 `strict=false`，因此运行时不能假设 Schema 已替它拒绝畸形参数。

**触发路径**

- number/text 字段携带 `"options": {}`：Schema 无效，但运行时把它当空数组；`UserDecisionModels.cs:411-416` 因空集合认为没有 selection 属性，最终请求可被接受。
- multi-select 字段携带 `"default_option_ids": "approve"`：Schema 无效，但运行时静默丢弃模型指定的默认值，用户看到的 Dialog 与工具调用语义不同。

**影响**

工具契约与实际行为不一致；模型畸形调用不会稳定返回 `invalid_request`，部分调用会被悄悄改写后展示给用户。测试只覆盖合法数组和未知属性，没有覆盖已知属性的错误 JSON 类型。

**建议**

只要属性存在就严格验证其 JSON 类型；错误类型统一抛 `UserDecisionValidationException` 并返回 `invalid_request`。为每个 optional property 增加 wrong-type 参数化测试，并增加一组“Schema 接受集合”和“运行时接受集合”一致性测试。

### Minor

#### M1. `formatting_changed` 永远返回 false，实际编码格式变化不会被报告

**位置**

- `LoomX/Assistant/Configuration/TomlDocumentService.cs:27`：写入编码固定为无 BOM 的 `new UTF8Encoding(false, true)`。
- `LoomX/Assistant/Configuration/TomlDocumentService.cs:231,235`：失败和成功结果均硬编码 `FormattingChanged = false`。
- `LoomX/Assistant/TomlTools.cs:309-316`：该字段作为公开工具结果返回给模型。

**触发路径**

输入是 UTF-8 BOM TOML，执行任何实际 set/delete/patch。读取时 BOM 被跳过，写入时使用无 BOM UTF-8，因此文件字节格式发生变化，但结果仍返回 `formatting_changed: false`。

**影响**

公开契约提供了不可置信的状态字段；调用方无法得知非目标格式/编码变化。文本注释和大部分布局可能仍保留，因此本项定为 Minor。

**建议**

读取阶段记录 BOM/换行/其他会被规范化的格式元数据，写后据实计算 `FormattingChanged`；或保留原 BOM 并仅在确实无法保持时设为 true。增加 BOM 输入的真实写入测试。

## Recommendations

1. **先修复两个 Critical 再合并**：建立统一的工具参数安全投影边界，并让 TOML 读取同时按路径、容器语义和内容形态脱敏。
2. 把 TOML 写入明确实现为带 commit point 的事务：替换后验证/恢复不再受调用方取消打断，并增加规范化路径锁和源版本冲突检测。
3. 让 Broker 对“事件已发布但无人 Claim”有可证明的终态，避免无限等待；使用同步屏障测试覆盖 unsubscribe/inactive 竞态。
4. 将 AskUser Schema 与运行时类型检查做成单一契约来源或至少做双向一致性测试，不要对错误类型做隐式缺省。
5. 扩展安全测试矩阵：Assistant tool-call Session、jsonl、UI/审批摘要、下一轮真实 ModelRequest、任意 Header 名、非敏感键下秘密值、JWT/Bearer、完整用户路径、Handler 拒绝前参数。
6. 修复后重新执行完整 `dotnet test`、Release build/publish、standalone 启动与 UI 生命周期回归，并生成新的可追溯时间戳产物。当前发布包可追溯，但包含上述生产缺陷。
7. `git diff --check` 当前仅报告计划和 OpenSpec tasks 两处 EOF 空行；这是流程 Markdown 排版，不影响生产编译，不作为阻塞 finding，可在后续文档整理时修正。

## Assessment

**Ready to merge: No.**

- Critical：2
- Important：4
- Minor：1

两个 Critical 均为运行时敏感数据边界失守：一处允许原始工具参数进入 Session/UI/持久化/后续模型请求，另一处允许自定义 Header 或非敏感键下的秘密值通过 `toml.get` 进入 ToolResult。另有 TOML 取消后事务状态不一致、并发 lost update、AskUser 无 Claim 永久等待和 Schema/运行时不一致等 Important 问题。修复并补齐相应真实链路回归测试前，不建议合并。
