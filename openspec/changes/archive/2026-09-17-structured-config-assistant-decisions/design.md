## Context

See `proposal.md` for the motivation and user-facing scope. 当前 LoomX 的 Assistant 工具以 `ToolDefinition` 注册，AgentLoop 已有工具风险与逐条审批桥接；桌面端已有 `AssistantViewModel`、`GlassDialogWindow` 和 Browser Bridge。项目尚无 TOML 依赖或通用文件修改服务。

本 Change 只建立通用基础能力。Codex Profile、Catalog Builder 和 `codex.configure` 由依赖 Change 实现；本 Change 不把 Codex 语义写入 TOML Engine 或 AskUser 核心。

## Goals / Non-Goals

**Goals:**

- 提供可复用的 TOML 文档解析、路径访问、结构化修改、验证、备份与原子写入。
- 在 TOML 写入失败时保护原文件，并让工具输出符合 LoomX SecretBoundary 约束。
- 将 Assistant 的用户决策建模为可暂停、可取消、可恢复的结构化请求。
- 复用现有桌面 Dialog 和 Assistant 生命周期，不引入 WebView 或第三方 Agent Runtime。
- 允许后续 Client Skill 通过 Browser Bridge 使用用户自己的 Chrome 获取公开资料，遇到网站挑战时交还用户。

**Non-Goals:**

- 不生成 Codex `model_catalog.json`。
- 不修改 Codex `config.toml`。
- 不管理或重启 Codex 进程，不实现 CDP 或 App-server 刷新。
- 不接入第三方搜索 API，不存储搜索 API Key，不实现反爬、验证码、登录墙或浏览器指纹绕过。
- 不要求 Router 在 Model 层声明或验证上游能力契约。

## Decisions

### 1. 使用语法树而不是反序列化后整文件重写

TOML Engine 使用 Tomlyn 2.10.1 的 syntax parser/document model，并启用 trivia 捕获。读取/验证使用同一解析路径；修改通过 key/value/table 节点和结构化 TOML 值完成。最终文本由语法树 writer 输出，尽量保留 comments、无关 section、未知字段和原有布局。普通 `TomlSerializer` POCO 往返不作为编辑路径，因为它会丢弃未知字段，不能满足用户文件编辑要求。

替代方案：字符串替换无法正确处理 TOML 类型和 nested table；反序列化到 `Dictionary` 后重新序列化无法可靠保留 comments 和用户格式；自研 parser 超出范围。

### 2. 路径采用分段数组并集中处理 TOML 值

工具 API 使用 `path: string[]`，不使用点号字符串作为唯一语法，避免 dotted key、数组表和 key 名包含点号时歧义。工具层将 JSON 值转换为受控 TOML value 节点，只接受 string、integer、number、boolean、array、object；不接受 secret 以外的任意对象扩展。

### 3. 写入采用事务式候选文档

所有 set/delete/patch 先在内存文档副本上完成，执行结构校验和重新解析，再按以下顺序落盘：

```text
Read → Compare → Backup → Temp Write → Parse Temp → Atomic Replace → Parse Target
```

如果候选内容与现有内容相同，则返回 no-op，不创建备份。备份使用目标目录内带 UTC 时间戳的文件名；临时文件也在同一目录，以便 Windows 原子替换保持在同一卷内。替换失败时不删除可恢复的原文件或备份。

### 4. TOML 工具与 Assistant 工具分层

新增通用文件服务负责文档生命周期和结构化编辑；`toml.*` ToolDefinition 只负责 JSON Schema、参数解析、SecretBoundary 脱敏和安全摘要。Codex 或其他 Client 的高层工具不直接操作语法树，而是调用该服务。

工具风险分级：

- `toml.read/get/validate` 为 Read；
- `toml.set/patch` 为 Write；
- `toml.delete` 为 Destructive，并继续遵循现有 Assistant 权限策略。

### 5. AskUser 使用独立 Broker，不复用工具审批结果

工具审批回答的是“是否允许执行这个工具”；AskUser 回答的是一个包含字段结果的业务决策。两者都可以由 AssistantService 暴露给 ViewModel，但使用不同的请求类型和完成通道。

`UserDecisionBroker` 负责：

- 接收结构化请求；
- 为每个请求分配 request id；
- 暴露 pending request 事件给 AssistantViewModel；
- 使用 `TaskCompletionSource` 挂起工具 Handler；
- 在提交、取消、会话停止或 ViewModel 卸载时完成请求；
- 防止重复完成和永久等待。

Assistant 侧注册一个 `assistant.ask_user` 工具，使模型可以按需要发起请求；UI 不直接解析模型自然语言，而是渲染固定字段类型。

### 6. UI 采用现有 Dialog 和状态模型

新增 AskUser ViewModel/控件复用现有 `GlassDialogWindow` 风格和资源，不引入 WebView。单选、多选、数字和文本字段在一个 Dialog 中渲染；提交前校验必填字段；取消返回明确的 `cancelled` 结果。Assistant 页面关闭时由 broker 取消所有 pending request。

### 7. 资料检索遵循能力优先、用户浏览器兜底

本 Change 不新增搜索 Provider。后续 Skill 可以先让 Assistant 使用当前模型已提供的搜索能力；如果模型没有搜索工具，则调用已有 `browser.open/read/wait`，要求用户在 Chrome 中完成登录、验证码或 JS challenge。Browser Bridge 只读取用户已经授权的页面，不实现安全机制绕过。

## Risks / Trade-offs

- [TOML 语法树写回可能改变局部空白] → 测试必须断言注释、无关 section、未知字段和语义保留；如果某类 trivia 无法保留，返回明确的 formatting_changed 摘要而不是声称完全无改动。
- [Windows 文件替换可能被编辑器或杀毒软件短暂占用] → 使用同目录临时文件、有限重试和备份；重试失败时保留原文件并返回可恢复路径。
- [Agent Handler 等待用户期间可能被取消] → Broker 绑定会话 CancellationToken，并为 UI 卸载和会话停止注册取消回调。
- [AskUser 请求可能包含模型误生成的敏感内容] → 只允许结构化字段和安全摘要，提交前执行敏感字段过滤；不把完整文件内容或 Secret 放入问题文本。
- [模型没有搜索能力且用户未连接 Chrome] → Assistant 必须报告“无法验证资料”，通过 AskUser 请求用户提供结论，不能伪造检索结果。

## Migration Plan

1. 增加 Tomlyn 依赖和通用服务，但不改变既有 SQLite 配置路径。
2. 注册新工具和 Broker；未触发新工具时既有 Assistant 行为不变。
3. 先运行 TOML/AskUser 单元测试，再运行完整 LoomX.Tests。
4. 后续 Codex Change 通过服务接口接入；若后续发现语法树 writer 无法满足某类格式保留要求，只替换编辑器适配层，不改变 Tool API。
5. 回滚时移除新工具注册和依赖；既有用户配置文件不需要迁移。
