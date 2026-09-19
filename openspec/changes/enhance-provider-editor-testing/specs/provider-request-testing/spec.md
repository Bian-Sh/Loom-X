## Purpose

为 Provider 配置提供一个轻量、可观察且安全的真实模型请求测试器，用于验证协议、模型、代理、自定义请求头和 CLI 身份模拟在普通或流式推理请求中的实际效果。

## ADDED Requirements

### Requirement: 用户可以配置并发送真实模型测试请求
测试 Tab MUST 仅允许用户从模型 Tab 已启用的真实模型中选择测试模型、选择常规或流式模式并编辑单行发送内容；发送内容默认 MUST 为“每日一言”。模型启用状态或模型集合变化时，测试模型列表 MUST 立即同步。系统 MUST 按当前 Provider 兼容类型构造并发送真实推理请求。

#### Scenario: 发送常规请求
- **WHEN** 用户选择启用模型、保留默认 Prompt 并以常规模式发送
- **THEN** 系统使用当前 Provider 配置发送一次非流式推理请求，并展示最终响应

#### Scenario: 发送流式请求
- **WHEN** 用户选择流式模式并发送请求
- **THEN** 系统按接收顺序持续追加所有 SSE `data:` payload；有效 JSON 压缩为保留 Unicode 的单行 JSONL，非 JSON 与 `[DONE]` 原样保留，完成事件也包含在最终结果中

#### Scenario: 请求执行中
- **WHEN** 请求已经发送且尚未成功、失败或超时
- **THEN** 输入框内发送按钮显示不可交互的动态等待状态，页面不提供停止或重试操作

#### Scenario: 当前测试模型被取消启用
- **WHEN** 用户在模型 Tab 取消当前测试模型，但仍有其他已启用模型
- **THEN** 测试模型下拉列表移除该模型并自动选择第一个仍启用的真实模型

#### Scenario: 没有启用模型
- **WHEN** 当前 Provider 没有任何已启用的真实模型
- **THEN** 测试模型选择为空、发送操作不可用，并以当前界面语言提示用户先前往模型 Tab 启用模型

### Requirement: 测试请求继承当前 Provider 的有效连接配置
测试请求 MUST 使用当前 Provider 的 Base URL、API Key、自定义请求头、代理开关和已应用的 CLI/UA 身份。测试服务与真实 OpenAI/Anthropic 路由 MUST 共用同一端点解析实现，测试模块 MUST NOT 单独归一化或修正真实路由将使用的 URL。请求摘要 MUST 在绑定 Provider 时立即生成，并随基础或高级配置变化实时更新。摘要只展示当前测试页不可见但会影响真实请求的信息，包括真实路由将使用的最终请求端点、代理状态、CLI 身份与版本和自定义 Header 数量；摘要 MUST NOT 重复展示 Provider、模型、常规/流式模式或请求 ID，也 MUST NOT 显示 API Key、Authorization 或 Header 值。

#### Scenario: 打开测试 Tab
- **WHEN** 当前 Provider 已绑定到测试面板
- **THEN** 页面在发送请求前即可展示基于当前内存配置计算出的真实请求摘要

#### Scenario: 基础或高级配置发生变化
- **WHEN** 用户修改 Base URL、兼容类型、代理、自定义 Header 或 CLI 身份
- **THEN** 测试 Tab 的摘要立即反映下一次请求将使用的端点和安全配置

#### Scenario: Base URL 与协议路径形成重复版本段
- **WHEN** 当前 Base URL 已包含 `/v1`，而真实协议路由仍会追加 `/v1/messages`
- **THEN** 测试摘要和测试请求使用与真实路由完全相同的 `/v1/v1/messages`，系统不得只在测试模块中静默修正该地址，并写入不含敏感内容的 Warning 日志
#### Scenario: 使用代理和 CLI 模拟
- **WHEN** Provider 已启用代理并应用 CLI 身份
- **THEN** 测试请求通过有效代理设置发送并携带对应 CLI 身份请求头，摘要显示代理与 CLI 安全信息

### Requirement: 测试器提供紧凑的执行状态与可诊断响应
测试 Tab MUST 使用单行输入框并将发送按钮嵌入输入框右侧。Response MUST 不展示独立标题，直接使用只读、可选择文本控件占满结果面板，并支持系统右键菜单和键盘复制。页面 MUST NOT 提供独立复制或可见清空按钮。请求结束后，用户全选 Response 全部内容并按 Delete 或 Backspace 时 MUST 清空结果；部分选择、空内容或请求执行中 MUST NOT 清空。

#### Scenario: 普通请求成功
- **WHEN** 上游返回符合当前协议的非流式成功响应
- **THEN** Response 展示解析后的模型文本

#### Scenario: 流式请求成功
- **WHEN** 上游返回流式成功响应
- **THEN** Response 依次展示全部 SSE `data:` payload 的 JSONL/原始行，并保留 usage、完成事件和 `[DONE]` 等非文本 delta 数据

#### Scenario: 全选清空响应
- **WHEN** 请求已结束且用户全选 Response 全部内容后按 Delete 或 Backspace
- **THEN** 系统清空当前响应和结果状态；部分选择或请求执行中不执行清空

#### Scenario: HTTP 或协议响应失败
- **WHEN** 上游返回非 2xx、无效 JSON、与当前协议不匹配的结构或流式错误事件
- **THEN** Response 展示受长度限制的上游原始响应；JSON 只进行缩进格式化，不修改字段和值

#### Scenario: 没有上游响应体的异常
- **WHEN** 请求发生网络、URL、代理配置或超时异常且没有可用响应体
- **THEN** Response 展示包含安全错误代码、可用状态码和安全说明的格式化 JSON

#### Scenario: 切换 Provider
- **WHEN** 用户在测试请求进行中切换到另一个 Provider
- **THEN** 系统在内部取消旧请求并清空旧 Provider 的测试上下文，避免响应串入新 Provider

### Requirement: 测试器保护敏感内容并将生命周期写入控制台
业务日志 MUST NOT 记录 API Key、Authorization、自定义 Header 值、Prompt、响应正文或流式片段。响应正文 MAY 在测试 Tab 中展示，但系统 MUST 对累计和渲染长度设置上限。测试服务 MUST 通过注入的 `ILogger<ProviderTestService>` 将准备、发送、收到响应、解析完成、完成、取消和失败等生命周期事件写入桌面端控制台。

#### Scenario: 记录测试请求生命周期
- **WHEN** 测试请求从准备阶段推进到完成或失败
- **THEN** 控制台日志包含 Provider、Model、协议、路径、模式、代理状态、状态码、内容类型、字节数和耗时等安全摘要；发现相邻重复版本段时额外记录只包含安全路径的 Warning

#### Scenario: 上游返回超长内容
- **WHEN** 普通或流式响应超过测试器展示上限
- **THEN** 页面只保留受限长度内容，应用继续正确释放响应资源