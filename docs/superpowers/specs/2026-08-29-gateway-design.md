# 网关路由编排设计

## 目标

网关固定提供 OpenAI、Ollama、Azure 三类对外 Endpoint。每个 Endpoint 独立维护模型路由副本，因此模型组合、启用状态和优先级互不影响。配置写入 SQLite，运行时周期性刷新快照。

## 配置契约

- `GatewayEndpointEntity` 保存稳定的 Endpoint 键、显示名称、公开路径和启用状态。
- `GatewayRouteEntity` 保存 Endpoint 与 `ModelEntity` 的引用、对外模型名、启用状态和排序值。
- 同一 Endpoint 不允许重复引用同一个 Model；删除全局 Model 前必须先删除其路由副本。
- 初始化时幂等创建三个 Endpoint：`openai`、`ollama`、`azure`。

## 桌面交互

网关页左侧显示三个 Endpoint 卡片，右侧显示当前 Endpoint 的路由副本。支持切换 Endpoint、启停 Endpoint、添加和删除模型、拖动调整优先级、启停路由和复制公开地址。所有操作立即保存，并显示一次性状态反馈。页面标题和说明由主窗口顶部提供，页面内容不重复渲染标题。

## URI 约定

- Ollama Endpoint 的 Base URL 为监听根地址 `/`，具体操作使用 `/api/tags`、`/api/show` 和 `/api/chat`；为兼容 OpenAI-compatible Ollama 客户端，同时提供根地址下的 `/v1/models` 和 `/v1/chat/completions`，这些路径仍归属 Ollama Endpoint。
- OpenAI Compatible Endpoint 的唯一前缀为 `/openai`，具体操作使用 `/openai/v1/models`、`/openai/v1/responses` 和 `/openai/v1/chat/completions`。
- Azure Endpoint 的唯一前缀为 `/azure`，具体操作使用 `/azure/v1/models` 和 `/azure/v1/responses`。
- `/v1` 不是独立 Endpoint；根地址下的 `/v1/models` 和 `/v1/chat/completions` 仅是 Ollama 的兼容操作，不能与 `/openai/v1/...` 混淆。

## 运行时

- OpenAI 客户端使用的 Endpoint Base URL 为 `/openai`，实际请求入口为 `POST /openai/v1/responses`；客户端负责拼接 `/v1/responses`。
- Ollama 继续提供原生接口；Azure 客户端使用 `/azure/v1`，实际请求入口为 `/azure/v1/responses`。
- Provider 未单独配置模型列表 URL 时，桌面端只在 Base URL 后追加 `/models`；Base URL 中是否包含 `/v1` 完全由用户配置。
- 请求模型名先在当前 Endpoint 的启用路由副本中解析，未指定模型时使用第一条启用路由。
- 路由按优先级尝试。网络异常、408、429、5xx 允许转移到下一条；成功或不可转移的 4xx 立即返回。
- OpenAI Provider 根据 `EndpointFormat` 调用 Responses 或 Chat Completions 上游；内部默认路径为 `/responses` 或 `/chat/completions`，不自动追加或剔除 `/v1`。Provider Base URL 是否包含 `/v1` 由用户遵循供应商手册自行配置，最终地址为 Base URL 加内部路径。
- Anthropic 保持 `/v1/messages` 上游路径；Ollama 保持 `/api/chat`，不对 `/api`、`/openai` 或 `/azure` 做自动处理。

## 日志与测试

日志仅记录 Endpoint、Provider、Model、路径、状态码、字节数和耗时，不记录密钥、请求体或响应体。测试覆盖 Endpoint/Route CRUD、独立排序与启停、Responses 路径、故障转移和全失败响应。
