---
comet_change: enhance-provider-editor-testing
role: technical-design
canonical_spec: openspec
---

# Provider 编辑器与真实请求测试器技术设计

## 1. 背景与设计目标

当前 Provider 页面把 `ApiMode`、`EndpointFormat`、API Key、代理、Header、CLI 身份和模型健康检查混排在三个 Tab 中；旧“测试连接”只请求模型列表，无法证明真实推理协议、代理和身份 Header 已生效。本次变更在不修改数据库结构、Gateway 外部 API 和模型 Tab 行为的前提下，将配置入口收敛为四个 Tab，并增加一个使用当前编辑快照的真实模型请求测试器。

设计遵循以下边界：

- OpenSpec delta spec 是需求事实源，本设计只说明实现方式。
- 已有 Provider ID、`ApiMode`、`EndpointFormat`、模型及 Header 数据保持可读。
- Prompt、响应正文、API Key、Authorization 和 Header 值不得进入业务日志。
- 测试状态、Prompt 和响应不落库，不生成历史记录。
- 所有 UI 文案进入现有 resx 本地化体系，颜色只使用动态资源。

## 2. 组件边界

### 2.1 兼容类型映射

新增 `ProviderCompatibilityOption`，集中维护三个 UI 选项：

| UI 选项 | ApiMode | EndpointFormat | 请求路径 |
| --- | --- | --- | --- |
| OpenAI Chat Completions | `openai` | `chat_completions` | `/chat/completions` |
| OpenAI Responses | `openai` | `responses` | `/responses` |
| Anthropic Messages | `anthropic` | 保留规范化默认值 | `/v1/messages` |

`ProviderEditorViewModel.SelectedCompatibility` 负责双向映射，保存仍写入现有字段。加载旧值时按字段组合反向解析；旧 `ollama` 或未知组合回退显示 OpenAI Chat，但在用户实际选择或保存前不主动重写已有业务 ID。兼容类型变化只修改协议字段，不修改 Provider ID。

### 2.2 Provider ID 生命周期

`ProvidersViewModel.NewProvider` 在对象加入集合前生成 `provider-<8位小写十六进制>`。生成器对当前 `Providers` 集合执行不区分大小写的冲突检查，配置服务现有唯一性校验继续作为最终保护。`BusinessId` 从基础 Tab 移除，但继续保留为持久化属性和日志安全标识；名称、兼容类型及 Base URL 变化均不得重新生成 ID。

### 2.3 测试请求服务

新增 `IProviderTestService` / `ProviderTestService`，职责仅包括：

1. 校验并规范化当前 Provider 测试快照。
2. 解析请求 URL、鉴权、自定义 Header、CLI 身份和代理客户端。
3. 构造三种协议的最小文本请求。
4. 通过 `IProviderExecutionPipeline` 发送普通或流式请求。
5. 将上游响应归一化为安全、可展示的进度和结果 DTO。

建议 DTO：

- `ProviderTestRequest`：Provider/Model 标识、Base URL、协议字段、API Key、Header、`UseProxy`、Prompt、模式和最大展示长度。
- `ProviderTestProgress`：阶段、文本增量、已累计字符数、是否截断及可用元数据。
- `ProviderTestResult`：请求 ID、状态、HTTP 状态码、内容类型、耗时、响应字节数、响应文本、错误代码和重试建议。
- `ProviderTestSummary`：协议、实际路径、代理摘要、CLI 摘要和 Header 数量；不包含任何敏感值。

普通请求使用 `ExecuteAsync`；流式请求使用 `ExecuteStreamingAsync`。URL 语义与现有生产客户端一致：OpenAI Base URL 末尾追加 `/chat/completions` 或 `/responses`，Anthropic 末尾追加 `/v1/messages`。构造前统一移除 Base URL 尾部斜杠，避免重复分隔符。

### 2.4 协议载荷与响应解析

- OpenAI Chat：`model`、单条 user message、`stream`；普通响应读取 `choices[0].message.content`，流式读取 SSE `choices[0].delta.content`。
- OpenAI Responses：`model`、`input`、`stream`；普通响应按 `output_text` 或 `output[].content[].text` 提取，流式读取 `response.output_text.delta`，完成事件为 `response.completed` 或 `[DONE]`。
- Anthropic Messages：`model`、`max_tokens`、单条 user message、`stream`；普通响应拼接 `content[].text`，流式读取 `content_block_delta.delta.text`，完成事件为 `message_stop`。

非 2xx 响应仍读取受限长度的安全错误摘要用于 UI，但日志只记录状态码、内容类型、字节数和耗时。JSON 或 SSE 无法解析时返回协议错误，不抛出未处理异常。普通和流式累计文本均使用同一截断器，默认上限由常量固定；达到上限后停止追加 UI 文本但继续正确释放响应资源。

### 2.5 代理与 HttpClient 生命周期

测试服务通过可注入的代理设置读取器取得全局代理模式、主机、端口、用户名和仅驻留内存的密码。`ProviderEditorViewModel.UseProxy` 为 false 或全局模式为 `direct` 时走直连；`system` 使用系统默认代理；`custom` 创建带 `WebProxy` 的 `HttpClientHandler`。自定义代理无效时返回配置错误，不静默改为直连。

直连客户端可由服务构造时注入以便测试复用；自定义代理客户端按单次执行创建并随请求释放，避免缓存包含旧凭据的 Handler。超时由链接 Token 和服务默认超时共同控制，用户停止只映射为“已取消”。

## 3. 测试面板 ViewModel

新增独立 `ProviderTestPanelViewModel`，避免继续扩大 `ProvidersViewModel`。它持有：

- 当前 Provider 引用和可选择模型列表。
- 默认 Prompt“每日一言”、常规/流式模式、选中模型。
- `IsRunning`、阶段、响应文本、请求摘要、HTTP 元数据、错误与截断状态。
- 发送、停止、重试、清空命令；复制由 View 代码后置调用剪贴板并通过 `ToastService` 提示。

绑定 Provider 时默认选择第一个启用的真实模型；没有启用模型时可展示禁用模型但发送不可用，并显示“先同步或添加模型”。切换 Provider 时先取消旧 `CancellationTokenSource`，再清空旧请求 ID、结果、错误和摘要，防止晚到增量写入新上下文。发送时从 Provider 和模型创建不可变快照，因此自动保存并不是测试前置条件；用户在内存中刚修改的 Base URL、API Key、Header、代理和兼容类型立即生效。

流式进度先在后台累计，再以短时间窗口批量派发到 Avalonia UI 线程，避免逐 token 刷新。所有状态更新在请求版本号校验后执行，已切换 Provider 或已取消请求的回调直接丢弃。

## 4. ProvidersViewModel 集成

`ProvidersViewModel` 新增只读 `TestPanel`，构造时可注入 `IProviderTestService` 以支持单元测试，默认使用真实服务。`SelectedProvider` 变化时调用 `TestPanel.BindProvider`。文化变化时同时刷新测试面板本地化；`Dispose` 时取消测试并释放服务资源。

旧 `TestConnectionCommand` 和编辑面板内连接状态块移除；顶部 Provider 健康统计、`VerifyAllProvidersCommand` 与健康服务保持不变。模型同步、拖放排序及模型编辑命令不改语义。

## 5. UI 布局

`ProvidersView.axaml` 调整为：

1. **基础**：名称、三张兼容类型卡片、Base URL、API Key；不渲染 BusinessId。
2. **高级**：代理、自定义 Header、CLI/UA 模拟；删除旧连接测试区块。
3. **模型**：保持现有内容和绑定。
4. **测试**：上方请求配置，中部安全摘要，下方终端式 Response 面板。

测试 Tab 使用现有 `Surface*Brush`、`BorderBrush`、`AccentBrush`、状态色资源，不根据透明主题截图硬编码颜色。发送时按钮切换为停止；完成或失败后显示重试、复制和清空。响应面板显示用户可见正文和安全元数据，不显示密钥及 Header 值。

## 6. 日志与错误处理

服务在开始、完成、取消和失败边界写结构化日志：Provider、Model、协议、路径、模式、代理状态、状态码、内容类型、字节数和耗时。异常对象作为日志首参数。禁止记录 Prompt、响应正文、流式片段、API Key、Authorization、自定义 Header 值和代理密码。

错误分类至少覆盖：配置无效、无模型、401/403、404/405、429、5xx、超时、用户取消、网络失败、非 JSON 普通响应和无效 SSE。UI 使用本地化安全摘要；可重试错误保留最近一次不可变请求快照，重试不会重新使用旧 Provider 的可变状态。

## 7. 测试与验证

按 TDD 顺序实施：

1. `ProviderCompatibilityOptionTests` 和 `ProviderEditorViewModelTests` 固定映射、旧值回显、自动 ID 与稳定性。
2. `ProviderTestServiceTests` 使用假 Handler/执行管线覆盖三协议普通与流式载荷、鉴权、Header、路径、代理、取消、错误、截断和敏感日志。
3. `ProviderTestPanelViewModelTests` 覆盖默认模型、默认 Prompt、命令状态、切换取消、重试和清空。
4. `ProvidersViewContractTests` 固定四 Tab、隐藏 ID、API Key 位置、高级 Tab 范围及测试绑定。
5. 本地化资源覆盖 zh-CN、en-US、zh-TW；沿用现有硬编码检查。
6. 运行定向测试、完整测试、Release 构建和 OpenSpec 严格验证。
7. 使用 CUA 后台启动发布包，验证四 Tab、三兼容类型、普通/流式、取消、错误、复制和清空；发布到 `outputs/<可读时间>` 并按进程 Path 校验启动文件。

## 8. 主要文件变更

- `LoomX/ViewModels/MainWindowViewModel.cs`：Provider ID、兼容映射接入、测试面板生命周期和旧连接块命令移除。
- `LoomX/ViewModels/ProviderTestPanelViewModel.cs`：新增测试交互状态与命令。
- `LoomX/ViewModels/ProviderCompatibilityOption.cs`：新增协议映射。
- `LoomX/Services/ProviderTestService.cs`：新增请求构造、发送、解析、代理和安全结果模型。
- `LoomX/Views/ProvidersView.axaml(.cs)`：四 Tab、测试 UI 和复制反馈。
- `LoomX/Resources/Strings*.resx`：新增和调整文案。
- `LoomX.Tests`：新增服务、ViewModel、映射、安全日志与视图契约测试。

## 9. 回滚策略

数据库字段和持久化格式不变。回滚时恢复旧 XAML、移除兼容映射与测试组件即可；已有 Provider 与模型数据无需迁移。若测试服务出现兼容问题，可单独隐藏测试 Tab 而不影响 Gateway 和 Provider 保存链路。
