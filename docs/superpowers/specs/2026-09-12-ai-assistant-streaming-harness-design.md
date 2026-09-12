# AI 助手真流式与会话设计

## 背景与目标

当前助手在 `ProviderExecutionPipeline.ExecuteAsync` 中完整读取响应，再由 `OpenAiCompatibleModelClient` 从字符串解析 SSE；`TextDelta` 因此并非网络到达时的增量。`AssistantSessionStore` 在一轮结束后原子重写单份 JSONL，只保存模型消息，不保存 reasoning 原文、逐条时间和可重建的处理阶段。UI 仅在新增消息时滚动，已有消息增长不会触发跟随。

本次把用户可见名称统一为“AI 助手”，优化现有 Avalonia 聊天区，不新增独立页面或面板。真流式是验收门槛：服务端尚未发送完成帧时，首个文本或思考增量已经到达 UI；取消能中断正在等待的网络读取。重新打开会话后仍可展开模型实际返回的 reasoning 原文、阶段性文字和处理步骤。

## 参考与取舍

- `D:/AppData/Github/aily-blockly` 当前主仓库已经移除旧 Angular Chat；实际运行的是 `@aily-project/subapp-aily-chat` 0.1.35，使用适配自 Pi `SessionManager` 的会话实现。每会话一份带 header、父子 ID 和时间的权威 JSONL；完整助手消息包含按顺序排列的 `thinking`、`text`、`toolCall` 内容块，在 `message_end` 追加。`message_update` 的 `thinking_delta`/`text_delta` 作为实时事件传给 UI；可重建的 sidecar 只负责索引，不是第二份消息日志。
- `D:/AppData/Github/PI-Desktop` 的运行事件与界面投影用于聊天展示：轮次和步骤边界、阶段性回复、思考与工具过程折叠。Loom-X 在最终回复完成时额外自动收起此前展开的过程；失败提示保持可见。
- Kun 的 `messages.jsonl`、`events.jsonl`、`metadata.jsonl` 服务于它的跨订阅者重放和序号游标，不作为本次必须复制的文件数量。取消先前拟增加第二份持久事件 JSONL 的方案。
- 不增加会话索引 sidecar；只有实际大历史读取性能成为问题时再加入可重建索引。沿用现有 `LiveMarkdown.Avalonia`，不引入新 Markdown 引擎。

## 模块边界

新增可单独引用的 `LoomX.Harness` .NET 类库。它定义会话、消息及有序内容块、模型/工具接口、Agent 循环、轮次/步骤生命周期和结构化事件；不引用 Avalonia、Loom-X 配置或服务。通用错误与取消状态在此表达，不把 Provider 错误文案和 UI 展示逻辑放入核心。

Loom-X 应用层继续拥有 Provider 选择及协议适配、Loom-X 工具与权限门、系统提示词、会话 JSONL 实现、敏感内容检查、日志、本地化及 Avalonia 投影。`AssistantService` 负责在核心事件和持久化/UI 消费者之间协调；UI 不读取 Harness 内部可变状态。现有工具定义可迁移到核心契约，具体工具处理器仍由应用层注册。网关原有 `ExecuteAsync` 行为保持不变。

## 网络流与事件

Provider 执行管线新增有明确响应所有权和异步释放语义的流式入口：收到响应头即可返回状态、头和可取消读取的正文流；非成功响应仍有受限大小的错误解析。助手按行/帧增量解析 Chat Completions 和 Responses SSE，不再经整包 `byte[]`、`StringReader` 或整段 Responses 转换桥接。现有网关缓冲/规范化调用不因此改变。

Chat Completions 的 `delta.content` 与实际存在的 `delta.reasoning_content` 等兼容字段分别成为文本和 reasoning 增量。Responses 的 `response.output_text.delta`、reasoning 原文事件、reasoning summary 事件、function call 参数事件和终态事件分别解析；summary 与原文不能混同。Provider 不提供原文时不合成原文，也不把隐藏的内部推理宣称为可取回。工具参数在协议层完整累积后交给核心执行；UI 只展示工具名称、状态及必要的安全摘要。

每轮产生 `turn_start`、每次模型调用产生 `step_start`；增量、消息完成、工具开始/结束、轮次完成/失败按发生顺序推送。一次步骤中的阶段性文本属于该步骤，后续工具调用不覆盖它；无工具调用的最后一次完整助手消息是最终回复。结束帧缺失或 SSE 中途异常属于失败，不把残缺内容当成成功答案。取消传入 `HttpClient`、正文读取、Agent 循环和工具等待链，及时释放响应。

## 单文件会话记录

每会话仍只有一份权威 JSONL。新版本首行是只承载静态标识的 `session` header，后续为带稳定 `id`、`parentId`、`timestamp` 的 typed entry：`message` 保存完整用户/助手/工具消息；助手消息保留有序 `thinking`、`text`、`toolCall` 内容块，reasoning 块标明 `raw` 或 `summary` 来源；少量 `custom` entry 保存 UI 重开后需要的处理阶段、工具安全摘要和完成/失败/取消状态。当前状态由最后一个生命周期 entry 推导，不能回写首行 header。模型上下文由 `message` entry 恢复，`custom` 不自动回传模型。单条消息和活动时间以 entry 时间为准。

流式 delta 只用于内存中的 UI 投影，不逐 token 落盘；每次 `message_end` 追加完整消息，每个需要历史展示的语义边界追加对应活动 entry。首次写入和版本迁移使用临时文件原子替换，后续正常记录顺序追加。读取时以有效完整记录为准，损坏的尾行不遮蔽先前消息；下次追加前处理不完整尾行，避免它吞并新记录。会话写入按会话串行化。

现有 v1 `meta`/`message` JSONL 可直接读取；下次写入该会话时将其原有消息一次性迁为新版本，不能重复追加。旧记录没有逐条真实时间和 reasoning，UI 不伪造原文；需要时间时明确采用历史会话的保存时间作为退化值。会话列表、删除和恢复 `Running` 为 `Cancelled` 的既有行为保留。

用户已接受 Aily/Pi 的取舍：进程异常退出时，尚未到 `message_end` 的半条文本或 reasoning 不保证恢复。已完成且成功落盘的消息和活动必须在重开后完整可见。不能把请求体、响应体或逐 token 内容写入 `ILogger`；持久化每条记录前沿用敏感信息兜底检查。若原文命中 Secret 检查，保留本次内存展示，标记当前会话从此不可继续持久化，停止后续依赖该内容的写入，并在现有状态区给出安全提示；先前有效记录保留，不静默声称当前轮次已经保存。

## 聊天投影与交互

一个轮次在现有消息列表中依次呈现：用户气泡、默认折叠的思考、阶段性回复、默认折叠的处理步骤、最终回复。每个阶段可再次出现，不把工具返回正文或模型协议帧直接堆成聊天消息。最终回复完成后自动折叠此前的思考和处理步骤，仍可手动展开；失败内容始终可见。

用户与 AI 气泡下均有时间和复制图标；用户气泡操作区平时隐藏、悬停和键盘聚焦时出现。已有“新会话”“发送”“停止”“批准”“拒绝”等命令改为语义明确的图标按钮，配置项保留合适的选择控件；图标提供 tooltip 和无障碍名称。模型摘要后显示当前思考等级。视觉沿用 Aily Blockly 的清爽密度和 Loom-X 现有配色，不引入额外装饰容器。

完整消息及增长中的 Markdown 使用同一投影，增量按顺序追加到已安装的 `LiveMarkdown.Avalonia` 构建器；不为每个 chunk 重建气泡。滚动行为以当前 `ConsoleView` 实现为准：默认跟随，距底部约 4px 内保持/恢复跟随，用户上滚后停跟随并显示跳至底部图标；新消息、现有消息内容增长和 Markdown 布局变化都只在跟随状态下滚到底部。重开或切换会话时恢复合理的底部位置，不由后台 delta 强行抢回滚动。

## 验证要求

- 用可控延迟的 HTTP/SSE 测试证明首个文本和 reasoning 增量在完成帧之前被消费；取消正在等待下一帧的读取后不再产生内容。覆盖 Chat Completions、Responses、压缩响应、工具参数分片、缺失完成帧和错误状态，网关既有缓冲路径测试继续通过。
- Harness 独立构建及单元测试覆盖多步骤、阶段性文本、工具调用、最终回复、超时/取消/失败，确保核心项目不依赖 Loom-X 或 Avalonia。
- 会话测试覆盖原文/摘要标识与顺序、时间和工具步骤回环、重开投影、v1 兼容与首次迁移、损坏尾行、并发写入和敏感内容拒绝；日志测试确保不含 prompt、reasoning、响应正文和工具参数。
- ViewModel 与 GUI 验证默认折叠/完成自动折叠、手动展开、复制/时间/tooltip、模型后思考等级、Markdown 流式更新、上滚停跟随及回到底部恢复。桌面/窄窗口检查内容不重叠。
- 修改此 Avalonia 桌面应用后重新发布，在 `outputs/` 中以可读时间命名产物，并启动发布包核对进程路径与实际界面。
