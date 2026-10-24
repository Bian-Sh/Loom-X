# Claude Code 接入 LoomX

## 目标

把 Claude Code 的请求经 LoomX 的 openai Endpoint（Anthropic 协议桥接）路由到上游。

## 标准流程（clientconfig.* 工具）

1. **检查现状**：`clientconfig.inspect { "client": "claude-code" }` → missing / unmanaged / managed / invalid。
   - invalid：`~/.claude/settings.json` 解析失败，工具会拒绝覆写；先请用户修复或备份。
2. **确认前置**：`loomx.list_endpoints` 确认 openai Endpoint 已启用、已绑定目标 Combo（`loomx.test_endpoint` 可体检）。
3. **写入**：`clientconfig.write { "client": "claude-code" }`
   - 工具写 `~/.claude/settings.json`：`env.ANTHROPIC_BASE_URL` 指向 LoomX（回环地址 + `/openai`，不带 `/v1`），`env.ANTHROPIC_AUTH_TOKEN` 写 Endpoint API Key，并在 `loomx.gateway` 留托管标记；用户手写的其他设置（permissions、hooks 等）原样保留。
   - 写入前自动备份为 `settings.json.bak-<时间戳>`。
   - Claude Code 不需要模型列表（模型由用户在会话内 `/model` 选择或默认）。
4. **验证**：新终端启动 `claude`，发送一条消息验证；失败先 `loomx.test_endpoint` 检查 Endpoint。
5. **回滚**：把 `settings.json.bak-<时间戳>` 复制回 `settings.json`。

## 注意

- **API Key 落盘**：Key 写进 settings.json（`ANTHROPIC_AUTH_TOKEN`），不回显在工具结果里；该文件含本机凭据，提醒用户勿外传。
- 既有配置里如已存在 `ANTHROPIC_BASE_URL`/`ANTHROPIC_AUTH_TOKEN`（指向其他网关），写入会覆盖；先 `clientconfig.inspect` 并向用户确认。
- Key 不进入聊天上下文；已暴露的 Key 建议轮换（Endpoint 设置里支持一键轮换）。

## 资料获取边界

1. 优先使用模型原生或已有的官方资料能力。
2. 其次使用 Browser Bridge 的 `browser.open`、`browser.read`、`browser.wait` 读取用户授权页面。
3. 遇到登录、CAPTCHA、Cloudflare 或 JS challenge 时立即暂停并交还用户，禁止绕过网站安全机制。
4. 没有可用资料通道时，使用 `assistant.ask_user` 请求用户提供资料或结论。
