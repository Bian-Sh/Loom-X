# 任务

- [x] 增加 Combo 名称声明与成员模型禁止绕过的回归测试
- [x] 在所有网关请求入口按 URL 解析 Endpoint，并复用该 Endpoint 的 Combo 匹配规则
- [x] 让各协议模型发现接口与对应 Endpoint 的请求入口共享 Combo 目录
- [x] 修正 Ollama 卡片 Base URL，运行测试与构建
- [x] 固化 `/`、`/openai`、`/azure` 唯一 Endpoint URI，移除重复 `/v1` 入口
