## Why

当前 Provider 编辑器将接口协议和 OpenAI 请求格式拆成两个技术字段，Provider ID 需要用户手工输入，API Key 与常用基础信息分离；现有“测试连接”仅探测模型列表，无法验证真实模型推理、流式响应、代理与 CLI 身份模拟是否生效。需要将配置入口收敛为更直观的兼容类型，并提供可观察、可重试的轻量请求测试器。

## What Changes

- 基础 Tab 隐藏 Provider ID，为新 Provider 自动生成稳定且唯一的内部业务 ID，已有 Provider ID 保持不变。
- 将 Provider 类型与 OpenAI 请求格式合并为三个接口兼容类型：OpenAI Chat Completions、OpenAI Responses、Anthropic Messages。
- 将 API Key 从请求 Tab 移至基础 Tab，并保留安全显示/隐藏交互。
- 将请求 Tab 更名为高级，仅保留代理、自定义请求头和 CLI/UA 身份模拟配置。
- 移除高级 Tab 内旧的模型列表连接测试区块，保留页面顶部健康统计与“验证全部”能力。
- 新增测试 Tab，可选择模型、常规或流式请求模式及 Prompt，并默认使用“每日一言”。
- 新增真实 Provider 请求测试能力，按当前兼容类型、代理、API Key、自定义 Header 和 CLI 身份发送请求，并展示请求安全摘要、状态码、耗时、响应大小、响应内容与可重试错误。
- 测试过程支持取消、清空、复制响应和流式增量显示；切换 Provider 时取消未完成测试并重置上下文。
- 新增相关中英文资源、单元测试、视图契约测试和发布验证。

## Capabilities

### New Capabilities
- `provider-request-testing`: 定义 Provider 编辑器内真实模型请求测试器的输入、配置继承、普通/流式执行、响应展示及安全边界。

### Modified Capabilities
- `provider-panel`: 调整 Provider 详情 Tab 结构、自动内部 ID、兼容类型选择、API Key 位置及高级配置范围。

## Impact

- 影响 Avalonia Provider 页面、Provider 编辑与测试 ViewModel、本地化资源和相关 UI 契约测试。
- 新增独立 Provider 测试服务和统一测试请求/响应 DTO，并复用现有 Provider 发送管线和代理配置读取能力。
- 不修改数据库结构，不迁移或重写已有 Provider ID，不改变模型 Tab、Gateway 对外 API 或运行时数据库路径。
- 日志继续只记录 Provider、Model、协议、路径、状态码、字节数和耗时等安全摘要，不记录密钥、Header 值、Prompt 或响应正文。
