# OpenCode 接入 LoomX

## 目标

把 OpenCode 的自定义 Provider 注册到 LoomX 网关（Chat Completions 协议，LoomX 统一桥接上游方言）。

## 标准流程（clientconfig.* 工具）

1. **检查现状**：`clientconfig.inspect { "client": "opencode" }` → missing / unmanaged / managed / invalid。
2. **确认前置**：`loomx.list_endpoints` 确认 openai Endpoint 已启用并绑定 Combo。OpenCode 无法自行发现模型，必须携带模型列表。
3. **写入**：`clientconfig.write { "client": "opencode" }`
   - 工具写 `~/.config/opencode/opencode.json`：`provider.loomx` 条目（npm `@ai-sdk/openai-compatible`、baseURL 带 `/v1`、apiKey、models 带 limit），并把根级 `model` 指到 `loomx/<第一个模型>`——不写根级 model 的话 provider 只是"可用"，用户仍要手动挑选。
   - 用户手加的其他 provider、mcp、theme 等配置原样保留；已存在的 loomx 条目原地更新（保留用户改过的名字）。
   - 写入前自动备份。
4. **验证**：新会话启动 `opencode`，`/models` 里确认 loomx provider 与模型，发消息测试。
5. **回滚**：把 `opencode.json.bak-<时间戳>` 复制回原文件。

## 注意

- **API Key 落盘**在 `provider.loomx.options.apiKey`；不回显，提醒用户勿外传。
- OpenCode 的凭据本有自己的 auth.json；本接入按 ai-switch 验证过的形态内联在 options 里。
- 模型列表以最近一次写入为准；LoomX 侧 Combo 变化后重新写入即可（CLI 下次启动生效）。

## 资料获取边界

1. 优先使用模型原生或已有的官方资料能力。
2. 其次使用 Browser Bridge 的 `browser.open`、`browser.read`、`browser.wait` 读取用户授权页面。
3. 遇到登录、CAPTCHA、Cloudflare 或 JS challenge 时立即暂停并交还用户，禁止绕过网站安全机制。
4. 没有可用资料通道时，使用 `assistant.ask_user` 请求用户提供资料或结论。
