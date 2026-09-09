## 修复方案

在独立的内部协议桥接组件中完成双向转换，避免把协议细节堆入路由处理器。

1. 入口继续接受 OpenAI Chat Completions JSON；当 `/v1/chat/completions` 路由选中的上游模型声明 `responses` 格式时，桥接组件将 `messages` 转为 `input`，转换 Chat Completions 的函数工具声明、`tool_choice` 与令牌上限字段，并保留安全的通用参数。
2. 上游请求仍由现有 `ProtocolPassthroughClient` 负责鉴权、配置 Header、遥测、可重试路由与安全日志。成功的 Responses SSE 在写回客户端前转换为 OpenAI Chat Completions SSE：文本增量成为 `delta.content`，函数调用参数增量成为 `delta.tool_calls`，完成事件输出终止块和 `[DONE]`。
3. 对非流式请求，将 Responses JSON 的文本或函数调用输出转换为 Chat Completions JSON。非成功响应仍按现有路径透传，避免改写上游错误语义。
4. 转换失败不记录正文或敏感字段，记录 Provider、Model、上游路径、内容类型、响应字节数和耗时，并让网关尝试下一条可用路由。

## 边界

- 仅 `/v1/chat/completions` 到 OpenAI `responses` 的组合路由使用桥接。
- OpenAI `chat_completions` 格式与原生 Ollama 上游保持直通。
- 不引入配置、持久化、公开 API 或数据库结构变更。

## 验证

- 使用伪造的 Responses SSE 覆盖文本、函数调用与终止事件转换。
- 断言上游接收到 Responses `input` 和扁平化函数工具定义。
- 执行相关单元测试、完整测试与构建。
- 重新启动本地服务后，在既有 VS Copilot 会话发送最小提示，确认助理正文显示。
