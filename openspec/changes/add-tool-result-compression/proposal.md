## Why

Agent 会话里工具调用结果（bash 输出、文件读取、grep 命中、测试与构建日志）会在每一轮请求中重复进入上下文，是输入 token 的主要来源之一。rtk（`https://github.com/rtk-ai/rtk`）的做法是在命令输出到达 LLM 上下文之前过滤和压缩：智能过滤、分组、截断、去重四种策略，宣称削减多达 90% 的 bash 输出。

LoomX 首先是 Router。Credential Protection change 已把「RTX Compression」列为该 change 的非目标，并明确未来压缩应实现于 Router、优先作为 Request Pipeline 的结构化处理阶段。本 change 按该规划落地：把 rtk 的过滤思想实现为第一方 Router 插件，在请求正文发送给 Provider 前压缩历史工具结果。

## What Changes

- 新增第一方插件 `plugins/LoomX.ToolResultCompression`：请求正文中的工具结果在进入 Provider 前按策略压缩，策略与阈值作为 Plugin-owned 配置存放在插件数据目录 `config.json`。
- 注册单个 Request Extension `toolresult.request`，失败策略 `continue-on-error`：压缩不是数据安全能力，处理失败必须放行原文，不得阻断 Router。
- 协议适配：OpenAI Chat Completions（`role: tool` / `role: function`）、OpenAI Responses（`input[].function_call_output`）、Anthropic（`content[].tool_result`）、Gemini（`contents[].parts[].functionResponse`）；未知协议同时尝试三种形态。
- 保真边界：只处理工具结果槽位，不触碰 system / user / assistant 内容；末尾 `KeepRecentToolResults`（默认 2）条工具结果保留原文，避免压缩模型即将引用的最新结果。
- 压缩策略：智能过滤（ANSI 转义、回车、冗余空行）、去重（连续重复行 `×N`、非连续高频行 `共 N 次`）、截断（长行截断并标注省略字符数、超行数保留头尾）、分组（git status 按状态分组、git log 只留哈希/作者/标题、git diff 折叠上下文、测试输出只留失败、长格式目录列表压缩为名称列表）、JSON 结构化折叠（数组截断、长字符串截断、超深层级折叠，输出保持合法 JSON）。
- JSON 折叠与请求重新序列化使用 `UnsafeRelaxedJsonEscaping` 紧凑输出，避免把中文重新转义成 `\uXXXX` 反而膨胀。
- 安全约束：长行截断不得破坏 `{{LOOMX_CREDENTIAL_...}}` 占位符，未闭合的占位符整体回退，保证 Credential Protection 的恢复链路不被压缩切断。
- 插件提供 `card-body` 声明式观测卡片（已压缩请求、累计节省与比例、异常与告警），多语言与宿主一致（zh-Hans / zh-Hant / ja / en）。
- 宿主接入：`LoomX.csproj` 新增该插件的 Build/Publish 负载拷贝目标（每个插件独立 OutputItemType，避免多个插件负载互相覆盖），`LoomX.slnx` 与 `LoomX.Tests.csproj` 新增项目引用，`PluginPublishContractTests` 扩展发布契约断言。
- 非目标：不新增 ToolResult ExtensionKind 或宿主侧 Tool Result Pipeline；不侵入 `AgentLoop`、`AgentSession` 或本地会话 JSONL；不调用模型做语义摘要；不修改 Provider 鉴权 Header 与路由逻辑。

## Capabilities

### New Capabilities

- `tool-result-compression`: 定义 Router 出站请求正文中工具结果的识别、压缩策略、保真边界、凭据占位符保护、失败放行与观测披露。

### Modified Capabilities

无。契约程序集、Plugin Runtime 与 Pipeline 语义不变，本 change 只新增一个插件与宿主发布负载。

## Impact

- 新增插件项目 `plugins/LoomX.ToolResultCompression` 及压缩器、请求重写器、配置存储、观测与 UI 文本实现。
- 新增测试：`LoomX.Tests/Plugins/ToolResultCompressorTests`、`ToolResultRequestRewriterTests`、`ToolResultCompressionPluginTests`（含经 `PluginRuntime` 动态加载并走 Request Pipeline 的集成验证），以及 `PluginPublishContractTests` 扩展。
- 修改 `LoomX/LoomX.csproj`、`LoomX.slnx`、`LoomX.Tests/LoomX.Tests.csproj`。
- 行为影响：内置 AI 助手与外部 Agent Client 的请求正文在满足条件时会被改写，模型看到的历史工具结果可能是压缩后的摘要形式；最近若干条结果保持原文。
- 观测只暴露计数与字符数（已压缩请求数、压缩条数、节省字符与比例、异常数），不记录任何工具结果内容。
- 插件默认启用；可通过插件自有 `config.json` 或 Runtime 的插件/Entry 启停关闭。
