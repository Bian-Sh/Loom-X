## Why

Visual Studio 的 Ollama OpenAI 兼容入口会向 `/v1/chat/completions` 发送 Chat Completions 请求，但当前在上游采用 Responses 协议的模型路由中直接透传该请求和 SSE 响应。上游成功返回的 Responses 事件不能被 Copilot 解析，导致助理消息容器为空。

## What Changes

- 为 `/v1/chat/completions` 的 OpenAI 上游 Responses 路由增加请求与响应协议桥接。
- 将 Chat Completions 请求转换为 Responses 请求，并将 Responses 的流式文本和工具调用事件转换回 Chat Completions SSE。
- 保持原生 Ollama 路由、OpenAI Chat Completions 上游直通、网关配置及数据库路径不变。
- 记录不含请求正文、响应正文、密钥或 Header 值的桥接诊断摘要，并补充回归测试。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

无。此变更修复既有 OpenAI 兼容入口的实现缺陷，不改变对外配置或新增产品能力。

## Impact

- 受影响代码：`OllamaHubHost`、协议透传服务及其测试。
- 受影响协议：Ollama 暴露的 OpenAI 兼容 `/v1/chat/completions`，仅当上游端点格式为 `responses` 时启用桥接。
- 不修改 Provider、Model、Gateway Combo 或本地设置数据库。
