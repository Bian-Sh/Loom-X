# Provider 请求后半段统一执行管线

## 背景

网关和小助手都直接调用 Provider，但当前分别实现了发送请求、读取响应、记录响应元数据和 OpenAI finish reason 规范化。两条路径的入口语义不同：网关必须保留 Endpoint/Combo 路由，小助手只使用用户选择的 Provider 模型并直连 Provider。因此本次只统一 Provider 请求发出后的底层执行阶段。

## 设计

新增 `IProviderExecutionPipeline`，输入已经构造好的 `HttpRequestMessage`、`HttpClient` 和安全的 Provider 上下文，输出状态码、内容类型、响应头、响应字节及可重试判断。管线不记录请求体、响应体、API Key 或 Authorization，只负责收集结果，并可对 OpenAI JSON/SSE 中空字符串 `finish_reason` 做兼容性规范化。

网关 `ProtocolPassthroughClient` 继续负责请求头复制、Endpoint 协议桥接、重试路由、下游响应写回和遥测；小助手 `OpenAiCompatibleModelClient` 继续负责 Responses 到 Chat Completions 的转换、SSE/toolcall 解析和模型错误分类。两者共享同一发送与响应收集实现。

助手首次使用时由 `GatewayProcessService` 惰性初始化同一套 DI 容器但不启动 Web 监听；只有用户显式启动网关时才启动该容器的 HTTP 服务。这样助手直连 Provider 不依赖概览页网关开关，同时避免复制配置库、偏好和工具注册逻辑。

助手模型选择移除“自动选择”用户语义：没有历史选择时仅将可用列表首个模型作为初始值并持久化；已有选择失效时返回无可用模型，不自动回退到其他 Provider/模型；助手不读取 Combo 或全局网关开关。

## 验证

- 管线测试覆盖响应元数据、正文读取、可重试状态和 finish reason 规范化。
- 网关现有转发/Responses bridge 测试继续通过。
- 助手测试覆盖首个模型初始选择、失效选择不回退和既有 toolcall/错误处理。
