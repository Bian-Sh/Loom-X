# Grok CLI 接入 LoomX

## 目标

把 Grok CLI 的请求经 LoomX 网关路由到上游（base-url 覆盖）。

## 鉴权现状（重要，先读）

Grok CLI 的代理鉴权环境变量名**尚未验证**（与 ai-switch 同一结论：未验证的键绝不猜测写入）。
因此本接入只写 base-url 键，不写鉴权键：

- 若 LoomX 的 openai Endpoint **未配置 API Key**，请求直接放行，本机回环场景下即可使用；
- 若 Endpoint 已配置 API Key，Grok CLI 无法自动携带，需要用户自己处理鉴权。

## 标准流程（clientconfig.* 工具）

1. **检查现状**：`clientconfig.inspect { "client": "grok" }`。
2. **确认前置**：`loomx.get_endpoint { "key": "openai" }`——重点看 `api_key_configured`：
   - 为 true：向用户说明鉴权限制，确认是否继续（或建议清空 Endpoint Key）。
3. **写入**：`clientconfig.write { "client": "grok" }`
   - 工具写 `~/.grok/settings.json`：`env.XAI_API_BASE_URL` 与 `env.GROK_API_BASE_URL` 指向 LoomX（回环地址 + `/openai`，不带 `/v1`），并在 `loomx.gateway` 留托管标记；用户手写的其他设置原样保留。写入前自动备份。
4. **验证**：新终端启动 grok CLI，发消息测试；失败先 `loomx.test_endpoint`。
5. **回滚**：把 `settings.json.bak-<时间戳>` 复制回 `settings.json`。

## 注意

- Endpoint 配置了 Key 时写入会成功但请求会被 401 拒绝——务必在写前检查并向用户说明。
- Grok CLI 无模型列表机制，模型名在会话内选择；LoomX 侧确保目标 Combo 已绑定 Endpoint。

## 资料获取边界

1. 优先使用模型原生或已有的官方资料能力。
2. 其次使用 Browser Bridge 的 `browser.open`、`browser.read`、`browser.wait` 读取用户授权页面。
3. 遇到登录、CAPTCHA、Cloudflare 或 JS challenge 时立即暂停并交还用户，禁止绕过网站安全机制。
4. 没有可用资料通道时，使用 `assistant.ask_user` 请求用户提供资料或结论。
