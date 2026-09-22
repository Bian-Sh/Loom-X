# 网关 Endpoint 凭据与 Ollama 思考等级设计

## 背景

网关页目前只展示 Endpoint 的公开 Base URL 和模型组合。OpenAI、Azure Endpoint
没有独立的客户端 API Key，公共路由也没有入站鉴权；Ollama Endpoint 在转发到
OpenAI 兼容上游时没有统一的思考等级配置。

LiveAgent 的 OpenAI 兼容实现采用标准思考等级梯子：`minimal`、`low`、`medium`、
`high`、`xhigh`、`max`。Responses 请求使用 `reasoning.effort`，Chat Completions
请求使用顶层 `reasoning_effort`。

## 目标与范围

- 为 OpenAI、Azure Endpoint 生成并持久化程序管理的 API Key。
- 在公共 OpenAI/Azure 路由校验客户端 API Key，错误时返回 HTTP 401。
- 在网关卡片中展示脱敏 API Key、复制按钮和悬停出现的刷新按钮。
- 为 Ollama Endpoint 增加思考等级下拉菜单，默认 `medium`。
- 在 Ollama 请求未显式提供思考参数时，将 Endpoint 等级注入正确的上游请求字段。
- 保持 OpenAI Endpoint 透传客户端思考参数，Azure 本次不增加思考等级配置。

本次不开放 API Key 自定义输入，不对 Ollama 增加 API Key，不调整 Provider/Model
级 API Key 语义，也不把 `/api/admin/*` 管理接口改造成公共客户端鉴权入口。

## 设计

### 配置与密钥生命周期

扩展 `GatewayEndpointEntity`，增加：

- `ProtectedApiKey`：OpenAI/Azure 的 DPAPI 密文；Ollama 始终为空。
- `ReasoningEffort`：Ollama 的 Endpoint 默认思考等级，默认 `medium`。

SQLite 初始化/迁移时为缺少 API Key 的 OpenAI、Azure Endpoint 生成随机值，使用
`RandomNumberGenerator` 产生至少 32 字节随机数据并以 URL-safe Base64 表示；数据库
只保存 `ProtectedApiKey`。刷新操作重新生成并覆盖密文，旧值立即失效。

管理响应仅通过桌面控制中心需要的 Endpoint 管理接口返回当前 API Key 明文，公共
模型列表、聊天响应、错误响应和日志不包含 API Key。API Key 的显示值由 ViewModel
生成脱敏文本（保留首尾少量字符，中间使用掩码），复制操作使用内存中的完整值。

Reasoning 等级只允许 `minimal`、`low`、`medium`、`high` 四档写入 Endpoint 配置。
读取到历史非法值时回退到 `medium`，避免阻断启动。

### 公共路由鉴权

新增按 Endpoint Key 解析的鉴权辅助逻辑，在公共处理器进入模型路由前执行：

- OpenAI：接受 `Authorization: Bearer <apiKey>`。
- Azure：接受 `Authorization: Bearer <apiKey>` 或 `api-key: <apiKey>`。
- Ollama：不要求 API Key。

比较使用固定时间比较；缺少、格式错误或不匹配时返回 `401 Unauthorized`，不记录
请求中的 Header 值。鉴权覆盖 OpenAI/Azure 的模型列表、Responses 和已有聊天入口；
Ollama 的 `/api/tags`、`/api/show`、`/api/chat`、`/v1/models` 和
`/v1/chat/completions` 保持无需鉴权。

`/api/admin/*` 继续作为桌面控制中心的本机管理接口使用，负责读取 Endpoint 明文
Key、刷新 Key 和保存思考等级；本次不扩大管理接口的鉴权改造范围。

### Ollama 思考等级转发

网关只在 Endpoint 为 Ollama 且入站请求未提供思考参数时注入配置值，客户端显式值
优先。注入按实际的上游请求格式处理：

- Chat Completions 上游：写入顶层 `reasoning_effort`。
- Responses 上游或 Ollama Chat 到 Responses 桥接：写入
  `reasoning: { "effort": "<level>" }`。

已有模型级 `Extra` 仍按现有规则合并，并保留模型级字段优先级；每条故障转移路由
从原始请求独立深拷贝，避免思考参数或模型字段泄漏到下一次尝试。OpenAI Endpoint
不主动补值，Azure 不增加 Endpoint 思考等级。

### 桌面端交互

`GatewayViewModel` 为 Endpoint 增加 API Key、脱敏显示、复制和刷新命令，以及 Ollama
思考等级选项和保存命令。

Endpoint 卡片布局保持 Base URL 行不变：

- OpenAI/Azure 在 Base URL 下方显示 API Key 文本和复制按钮。
- API Key 行悬停时，在复制按钮左侧显示刷新图标；未悬停时不占用额外视觉重点。
- Ollama 隐藏 API Key 行，显示 `Reasoning effort` 下拉菜单。
- 刷新、复制、保存成功或失败均使用现有 `ToastService` 提供短暂反馈；状态栏保留
  详细过程状态。

### 测试

- 配置数据库：旧库补列、默认 Endpoint 初始化、Key 生成/轮换、DPAPI 密文不保存明文。
- 管理服务：Endpoint 响应、刷新 Key、Reasoning 等级校验与非法值回退。
- 网关处理：OpenAI/Azure 缺 Key 或错误 Key 返回 401；Bearer 和 Azure `api-key`
  均可用；Ollama 无 Key 可访问。
- 请求装配：Ollama 默认等级注入、客户端显式值优先、Responses/Chat Completions
  字段形态正确、故障转移尝试互不污染。
- Avalonia 契约：OpenAI/Azure 的掩码 Key 与复制/刷新控件、Ollama 下拉选项及悬停
  可见性；刷新和复制命令绑定正确。

## 非目标

- 不允许用户手工输入或编辑 Endpoint API Key。
- 不为 Ollama 生成或校验 API Key。
- 不为 Azure 增加 reasoning effort UI 或改变其现有请求透传策略。
- 不将管理 API 暴露为新的远程管理协议。
