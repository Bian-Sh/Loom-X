# OpenClaw 接入 LoomX

## 目标

把 OpenClaw 的自定义 Provider 注册到 LoomX 网关（openai-completions 协议）。

## 标准流程（clientconfig.* 工具）

1. **检查现状**：`clientconfig.inspect { "client": "openclaw" }` → missing / unmanaged / managed / invalid。
   - invalid：文档声明 JSON5，但本工具只按严格 JSON 解析；带注释的文件会拒绝覆写，请用户先转成标准 JSON。
2. **确认前置**：`loomx.list_endpoints` 确认 openai Endpoint 已启用并绑定 Combo。OpenClaw 无法自行发现模型，必须携带模型列表。
3. **写入**：`clientconfig.write { "client": "openclaw" }`
   - 工具写 `~/.openclaw/openclaw.json`：`models.providers.loomx` 条目（baseUrl 带 `/v1`、apiKey、api=openai-completions、models 带 contextWindow/maxTokens），`models.mode` 仅在缺省时补 `merge`（用户显式的 `replace` 不回改），并把 `agents.defaults.model.primary` 指到 `loomx/<第一个模型>`。
   - 用户手加的其他 provider、fallbacks、mcp 原样保留。
   - 写入前自动备份。
4. **验证**：OpenClaw 网关会热加载该文件；确认模型列表里出现 loomx 条目并发消息测试（已运行会话保持启动时的模型）。
5. **回滚**：把 `openclaw.json.bak-<时间戳>` 复制回原文件。

## 注意

- **API Key 落盘**在 provider 条目 `apiKey`；不回显，提醒用户勿外传。
- 模型列表以最近一次写入为准；LoomX 侧 Combo 变化后重新写入。

## 资料获取边界

1. 优先使用模型原生或已有的官方资料能力。
2. 其次使用 Browser Bridge 的 `browser.open`、`browser.read`、`browser.wait` 读取用户授权页面。
3. 遇到登录、CAPTCHA、Cloudflare 或 JS challenge 时立即暂停并交还用户，禁止绕过网站安全机制。
4. 没有可用资料通道时，使用 `assistant.ask_user` 请求用户提供资料或结论。
