# Codex CLI 接入 LoomX

## 目标

把 Codex CLI（ChatGPT）的请求打到 LoomX 的 OpenAI Endpoint，由 LoomX 路由到上游。

## 核心原则：ComboModel + model_catalog_json

- LoomX 的 **ComboModel 是对 Codex 暴露的稳定模型身份**；Provider、Credential、实际模型、Model Mapping、优先级、故障转移全部由 LoomX Router 内部编排，不进入 Codex 配置。
- LoomX 会把选定 Combo 的模型投影为 Codex 启动时加载的 Model Catalog（`loomx-model-catalog.json`）。Catalog 变化需重启 Codex 生效；LoomX 内部的 Provider 增删、故障转移不需要。

## 标准流程（clientconfig.* 工具）

1. **检查现状**：`clientconfig.inspect { "client": "codex" }` → missing / unmanaged / managed / invalid。
   - invalid：既有 config.toml 语法非法，工具会拒绝覆写；先请用户修复或备份。
2. **确认前置**：`loomx.list_endpoints` 确认 openai Endpoint 已启用、已绑定目标 Combo、已配置 API Key（`loomx.test_endpoint` 可体检）。
3. **写入**：`clientconfig.write { "client": "codex" }`
   - 默认写 openai Endpoint；用 `endpoint` 换目标，用 `combo_ids` 精确选择暴露哪些 Combo（缺省用 Endpoint 绑定的启用 Combo）。
   - 工具自动完成：备份 config.toml（`*.bak-<时间戳>`）→ 设置 `model_provider = "loomx"` → 写 `[model_providers.loomx]`（base_url 带 `/v1`、`wire_api = "responses"`、`experimental_bearer_token`）→ 生成 `loomx-model-catalog.json` → 保留用户手写的其他配置。
   - 顶层 `model` 保留用户已选且仍在目录中的模型，否则落到第一个 Combo。
4. **验证**：`codex --help` 确认安装；新会话发一条消息测试连通；失败先 `loomx.test_endpoint`。
5. **回滚**：把 `config.toml.bak-<时间戳>` 复制回 `config.toml`。

## 注意

- **API Key 落盘**：Key 写进 `config.toml`（`experimental_bearer_token`），不回显在工具结果里；该文件含本机凭据，提醒用户勿外传。
- `auth.json` 里的官方登录态不会被使用，也不要删除。
- 改了 Combo 绑定/模型列表 → 重新 `clientconfig.write` 并重启 Codex；只改 LoomX 内部路由 → 无需动 Codex。
- 不需要重启 LoomX。

## 资料获取边界

1. 优先使用模型原生或已有的官方资料能力。
2. 其次使用 Browser Bridge 的 `browser.open`、`browser.read`、`browser.wait` 读取用户授权页面。
3. 遇到登录、CAPTCHA、Cloudflare 或 JS challenge 时立即暂停并交还用户，禁止绕过网站安全机制。
4. 没有可用资料通道时，使用 `assistant.ask_user` 请求用户提供资料或结论。
