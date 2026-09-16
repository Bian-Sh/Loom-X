---
comet_change: structured-config-assistant-decisions
role: technical-design
canonical_spec: openspec
language: zh-CN
status: approved
---

# LoomX 结构化配置与 Assistant 用户决策技术设计

## 1. 背景与范围

当前 Assistant 已具备 `ToolRegistry`、`AgentLoop`、逐条工具审批、`AssistantService` 会话门面、桌面 `AssistantViewModel` 和 Browser Bridge，但缺少两类可复用基础设施：一是安全、可验证且尽量保留用户格式的 TOML 结构化编辑；二是模型能够暂停当前步骤、一次收集多字段业务决策并在用户提交后恢复执行的 AskUser 通道。

本变更只建立通用能力，不包含 Codex Catalog、Codex `config.toml`、用户环境变量、Codex 进程管理或重启策略。后续 Client 集成只能调用本变更公开的服务与工具，不得把 Codex 语义反向写入 TOML Engine 或 AskUser 核心。

## 2. 架构边界

实现分为四个独立层次：

```text
Assistant 模型
   │
   ├─ toml.read/get/validate/set/delete/patch
   │        │
   │        ▼
   │   TomlTools（Schema、参数、风险、脱敏）
   │        │
   │        ▼
   │   ITomlDocumentService
   │        ├─ Tomlyn 语法树编辑
   │        └─ 备份/临时文件/原子替换
   │
   └─ assistant.ask_user
            │
            ▼
       UserDecisionBroker ──pending event──> AssistantViewModel
            ▲                                  │
            └──── submit/cancel result ── AskUserDialog
```

- `LoomX.Harness` 保持通用 `ToolDefinition`、风险等级和 AgentLoop 契约，不引入 Avalonia 或文件格式依赖。
- `LoomX/Assistant/Configuration` 承载 TOML 领域契约与文档服务。
- `LoomX/Assistant` 承载工具适配、敏感边界与 `UserDecisionBroker`。
- `LoomX/ViewModels`、`LoomX/Views` 只负责 AskUser 展示、字段校验、提交与取消。
- 所有运行诊断通过注入的 `ILogger<T>` 写入既有 Serilog 管线。

## 3. TOML 领域契约

### 3.1 路径与值

路径统一使用非空 `IReadOnlyList<string>`，工具 JSON 以字符串数组表示。每个 segment 必须非空，按 TOML key 的真实值处理；不得把 `a.b` 自动拆成两段，因此 dotted key 与包含点号的 quoted key 不会混淆。

受控值只接受：

- `string`
- 有符号 64 位整数
- `double`
- `bool`
- 上述值的数组
- 字符串键到受控值的对象/内联表

首版不向工具开放日期时间、无穷值、自定义 CLR 对象或数组表创建；读取遇到这些合法 TOML 类型时返回类型名和安全摘要，修改时若调用方未提供受支持值则明确拒绝，而不是猜测转换。

### 3.2 结果模型

服务返回结构化结果，而不是把完整文档直接交给模型：

- `TomlValidationResult`：`IsValid`、解析诊断位置与安全消息。
- `TomlReadResult`：文件路径安全摘要、文档是否存在、可公开的结构摘要。
- `TomlValueResult`：`Found`、`ValueType`、脱敏后的值。
- `TomlWriteResult`：`Changed`、`BackupPath`、`FormattingChanged`、验证状态与安全错误。
- `TomlPatchOperation`：`set` 或 `delete`、分段路径、可选值。

文件绝对路径只用于服务内部和用户明确指定的结果；日志默认记录文件名、扩展名或不可逆摘要，不记录完整用户目录结构。

## 4. TOML 文档处理

### 4.1 解析与查询

`TomlDocumentService` 使用同一套 Tomlyn syntax parser 处理 read/get/validate/write，启用 trivia 捕获。查询通过语法树中的 table/key/value 节点解析，不通过 POCO 或 `Dictionary` 全量往返，以避免丢失未知字段与注释。

读取流程：

1. 校验文件路径与输入大小上限。
2. 使用异步文件 API 读取 UTF-8 文本；不存在文件由具体操作决定是返回 not-found 还是创建空候选文档。
3. 解析并收集行列诊断。
4. 按分段路径定位节点，区分“不存在”与存在但值为空字符串等合法值。
5. 在工具边界执行递归脱敏后返回。

### 4.2 内存 Patch

所有写操作归一化为一次 `PatchAsync`：

1. 读取并解析原文。
2. 深复制或重新解析为候选语法树。
3. 顺序应用全部 set/delete；任一操作非法则整批失败。
4. 使用 Tomlyn writer 生成候选文本并重新解析。
5. 比较候选文本与原文；相同则返回 no-op。

`set` 在父表不存在时按路径创建普通 table；如果路径穿越标量、数组表或产生重复 key，则拒绝整批 Patch。`delete` 只删除目标 key/value，不自动删除空父表，避免扩大修改面。

### 4.3 事务式写入

实际落盘遵循：

```text
Read → Patch Candidate → Validate Candidate → Compare
     → Backup → Temp Write → Validate Temp → Atomic Replace → Validate Target
```

- 备份和临时文件均位于目标目录，文件名包含 UTC 时间戳与随机后缀。
- 现有目标文件先创建备份；新文件无原文件可备份时明确返回 `BackupPath = null`。
- Windows 优先使用 `File.Replace`；目标不存在时使用同目录 `File.Move`。对共享冲突或杀毒软件短暂占用执行少量、带取消令牌的延迟重试。
- 最终验证失败时优先从备份恢复；恢复失败同时返回目标、备份和错误类型的安全摘要，绝不报告成功。
- 成功和 no-op 后清理本次创建且不再需要的临时文件；保留成功写入前的备份供用户恢复。

## 5. 敏感信息边界

新增统一 `SensitiveKeyPolicy`，按不区分大小写的规范化 segment 识别 `key`、`api_key`、`token`、`password`、`secret`、`authorization`、`credential` 等名称及常见后缀。策略被 TOML 服务安全摘要、TomlTools、AskUser 和测试共同复用，避免多个不一致的正则表达式。

规则：

- `toml.read/get` 对敏感路径返回固定占位符和类型，不返回原值。
- `toml.set/patch` 的成功结果只包含操作数量和路径安全摘要，不回显写入值。
- 日志不记录完整文档、Patch JSON、请求/响应正文、Header 值或用户自由文本。
- 异常日志把异常对象作为第一个参数，并记录操作、文件摘要、阶段、错误类型、耗时。
- AskUser 的标题、问题、选项、影响摘要和默认值在进入 Broker 前做长度限制与敏感模式拒绝；用户提交结果不写日志或 Toast。

## 6. TomlTools 接入

新增独立 `TomlTools.RegisterAll`，由 `LoomXHost` 在现有 `LoomXTools` 与 `BrowserTools` 注册旁注入。六个工具保持稳定命名：

| 工具 | 作用 | 风险等级 |
| --- | --- | --- |
| `toml.read` | 返回文档结构与安全摘要 | Read |
| `toml.get` | 返回单一路径的类型与脱敏值 | Read |
| `toml.validate` | 返回解析诊断 | Read |
| `toml.set` | 单字段 set 的 Patch 简写 | Write |
| `toml.patch` | 原子应用多项 set/delete | Write |
| `toml.delete` | 单字段 delete 的 Patch 简写 | Destructive |

工具层负责 JSON Schema、参数解析、路径与文档大小限制、调用服务、把结果序列化为安全 JSON。文件系统读写只能经服务完成，Handler 不拼接 Shell 命令。

## 7. AskUser 契约与 Broker

### 7.1 请求模型

`UserDecisionRequest` 包含 request id 之外的业务数据：标题、问题、可选说明、影响摘要、是否允许取消及字段列表。字段使用判别类型：

- `single_select`：选项 id、标签、说明、可选默认值。
- `multi_select`：选项集合、默认 id 集合、最少/最多选择数。
- `number`：默认值、最小值、最大值、步长。
- `text`：默认值、是否多行、最大长度。

字段 id 在单个请求中必须唯一。默认值必须满足字段约束；未知选项、非法范围、空必填文本或重复 id 在进入等待状态前失败。

结构化结果使用字段 id 映射到字符串、字符串数组、数字或文本；取消结果单独使用 `Cancelled = true`，不得把默认值当作用户提交。

### 7.2 Broker 生命周期

`UserDecisionBroker` 是 singleton，并发安全地维护 pending request：

1. `RequestAsync` 校验并分配不可预测 request id，创建 `TaskCompletionSource<UserDecisionResult>`，使用 `RunContinuationsAsynchronously`。
2. Broker 发布 `PendingRequested` 事件；UI 订阅者只接收安全、不可变的请求快照。
3. 工具 Handler await 该任务，AgentLoop 因而停留在当前工具调用而不会丢失 Session 消息。
4. `Submit` 再次校验结果并完成任务；`Cancel` 返回结构化取消；重复提交/取消返回 false。
5. 调用方 CancellationToken、会话停止、页面卸载或 Broker dispose 会取消对应请求并从 pending 集合移除。
6. 所有完成路径先原子移除 pending，再完成任务，避免事件重入或双重完成。

`assistant.ask_user` 为 Read 风险的交互工具，不触发写工具审批；它只收集业务决策，本身不修改配置。工具返回安全的结构化 JSON，取消时使用成功的工具结果加 `cancelled: true`，让模型能解释取消并停止后续高影响操作。

## 8. Assistant 与桌面 UI 集成

`AssistantService` 注入 Broker，并在开始 Run 时把当前运行 CancellationToken 传给 AskUser Handler；`Stop`、新会话或服务释放时取消当前运行和关联 pending request。工具审批继续沿用 `ApprovalHandler`，不与 AskUser 合并。

`AssistantViewModel` 在激活时订阅 Broker，在停用/Dispose 时解除订阅并取消当前页面拥有的 pending request。收到请求后调度到 Avalonia UI 线程，创建 `AskUserDialogViewModel` 并打开基于 `GlassDialogWindow` 视觉体系的专用 AskUser Dialog。专用 Dialog 使用动态内容区承载字段控件，而不是把复杂表单塞入现有 420×220 删除确认模板。

提交前由 ViewModel 做即时校验，Broker 再做最终校验。提交成功、取消和错误分别通过 `ToastService` 显示短安全摘要；Toast 不包含用户选择值。Dialog 关闭等价于取消，且只有拥有该 request id 的 ViewModel 可以完成请求。

## 9. 资料收集约束

本变更只补充 Assistant/Skill 文档与系统提示约束：

1. 优先使用当前模型原生可用的资料/搜索能力。
2. 不可用时复用 `browser.open`、`browser.read`、`browser.wait` 等现有 Browser Bridge 工具读取用户授权页面。
3. 登录、验证码、Cloudflare 或 JS challenge 出现时暂停，提示用户自行完成。
4. 没有可用资料通道时调用 `assistant.ask_user` 请求用户提供结论或文档内容。

不得新增搜索 API Key、爬虫、Cookie 注入、TLS/浏览器指纹伪装或安全挑战绕过。

## 10. 错误处理与可观察性

- 解析错误：返回行列位置与安全消息，不修改文件。
- Patch 错误：返回失败操作索引与错误类型，原文件不变。
- 写入错误：记录失败阶段；保留原文件和备份，返回可恢复摘要。
- UI 无订阅者：AskUser 在限定时间内或会话取消时收敛，不永久等待。
- 用户取消：工具返回 `cancelled`，Assistant 不继续假定已授权的配置操作。
- 所有开始、完成、降级和失败事件使用结构化日志；成功为 Information，可恢复占用/格式变化为 Warning，异常为 Error，仅细节为 Debug。

## 11. 测试策略

### 11.1 TOML 单元测试

- 值转换：字符串、整数、浮点、布尔、数组、对象以及不支持类型。
- 路径：nested table、dotted key、quoted key、包含点号的 key、数组表和标量穿越。
- 解析：合法/非法 TOML、诊断位置、UTF-8、空文件和输入大小限制。
- Patch：set/delete/批量失败原子性、父表创建、重复 key、注释/未知字段/无关 section 保留。
- 文件事务：no-op、备份、临时文件、目标不存在、Windows 空格/中文路径、占用重试、替换失败、写后验证和恢复失败。
- 安全：所有敏感键和嵌套值不进入 ToolResult、日志捕获器或异常摘要。

### 11.2 AskUser 与集成测试

- 模型序列化与校验：重复字段 id、非法默认选项、min/max、必填与取消。
- Broker：并发 request id、提交、取消、CancellationToken、页面卸载、重复完成和事件异常隔离。
- 工具：`assistant.ask_user` Schema、等待恢复、取消结果、超时和敏感内容拒绝。
- Assistant：AgentLoop 在工具等待期间保持会话上下文，提交后继续下一步，Stop 后无 pending request。
- ViewModel/Dialog：四类字段组合、默认值、校验消息、关闭取消、Toast 安全摘要和透明主题下的可用性。

### 11.3 回归与交付

运行新增定向测试后执行完整 `dotnet test LoomX.slnx`，再执行 OpenSpec strict validate。验证通过后按 standalone 应用约定重新发布，把产物放入 `outputs/` 下带可读时间的目录。数据库路径、日志边界、现有工具审批与 Browser Bridge 行为不得回归。

## 12. 预计文件落点

- `LoomX/Assistant/Configuration/`：TOML 契约、值转换、敏感策略与文档服务。
- `LoomX/Assistant/TomlTools.cs`：六个工具定义。
- `LoomX/Assistant/UserDecisions/`：请求/字段/结果模型、校验器和 Broker。
- `LoomX/Assistant/AssistantTools.cs` 或现有工具注册附近：`assistant.ask_user`。
- `LoomX/LoomXHost.cs`：DI 与工具注册。
- `LoomX/ViewModels/AskUserDialogViewModel.cs`、`LoomX/Views/AskUserDialog.*`：桌面交互。
- `LoomX.Tests/Assistant/`、`LoomX.Tests/Views/`：单元、集成与契约测试。

实际文件可按现有命名约定微调，但上述层次边界和依赖方向不变。
