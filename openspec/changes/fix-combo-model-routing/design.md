# 修复设计

在配置解析层按 Endpoint 建立 Combo 目录。请求 URL 先确定 Endpoint，再只从该 Endpoint 的已启用 Combo 中解析请求 `model` 字段声明的 Combo；不跨 Endpoint 查找，也不根据其他 Endpoint 的同名 Combo 猜测。匹配时只比较 Combo 名称；成员 `ModelId`、显示名和 Ollama 名称不能绕过 Combo。各协议入口和对应模型发现接口复用当前 Endpoint 目录；未命中时仍返回原有 404。

网关页地址显示遵循客户端 Base URL 约定：Ollama 卡片显示监听根地址，由 Ollama 客户端自行追加 `/api`；OpenAI 和 Azure 卡片保留各自协议路径。

Endpoint URI 必须唯一：Ollama 使用 `/`，OpenAI 使用 `/openai`，Azure 使用 `/azure`。根地址下的 `/v1/models` 和 `/v1/chat/completions` 是 Ollama 的 OpenAI-compatible 操作，不是另一个 OpenAI Endpoint；正式 OpenAI 入口只有 `/openai/v1/...`。

不修改数据库结构、公开接口或 `/v1/models`、`/api/tags` 的返回格式。
