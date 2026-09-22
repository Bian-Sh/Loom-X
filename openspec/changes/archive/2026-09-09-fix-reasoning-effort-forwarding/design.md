# 修复方案

在 `OllamaHubHost` 中抽取统一的网关尝试请求装配方法：深拷贝客户端请求，替换当前路由的真实模型 ID，并将 `ResolvedModelConfig.Extra` 深拷贝合并到请求顶层。每条路由均从原始请求重新装配，避免上一条路由的模型配置泄漏到下一条路由。

`HandleResponsesAsync` 使用该装配结果继续执行现有的 Responses 桥接或协议透传；`HandleChatCompletionsAsync` 复用同一方法，保持其现有模型级额外字段覆盖行为。请求体、密钥和日志内容不新增敏感信息。

测试直接验证装配结果包含模型级 `reasoning_effort`，并验证原始 JSON 不被修改、嵌套额外字段使用深拷贝。
