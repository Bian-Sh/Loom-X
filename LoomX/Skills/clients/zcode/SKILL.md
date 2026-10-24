# ZCode 接入 LoomX

## 目标

把 ZCode 的自定义 Provider 注册到 LoomX 网关，Codex 模式走 openai Endpoint（openai-responses），Claude 模式走 Anthropic 桥接（anthropic-messages）。

## 标准流程（clientconfig.* 工具）

1. **检查现状**：`clientconfig.inspect { "client": "zcode-codex" }`（或 `zcode-claude`）。
   - 两个目标共享 `~/.zcode/v2/provider_config.json`，按托管标记区分彼此，互不覆盖。
   - invalid：既有配置解析失败会拒绝覆写（ZCode 遇到非法文件会整份退化为空列表，绝不能覆写）。
2. **确认前置**：`loomx.list_endpoints` 确认目标 Endpoint 已启用并绑定 Combo。ZCode 无法自行发现模型，写入必须携带模型列表——用 `combo_ids` 指定（缺省取 Endpoint 绑定的启用 Combo）。
3. **写入**：`clientconfig.write { "client": "zcode-codex" }` 或 `{ "client": "zcode-claude" }`
   - 工具写 `~/.zcode/v2/provider_config.json`：注册 `loomx-codex` / `loomx-claude` provider 条目（api-key 访问、托管标记头、模型规则含 contextWindow/inputFormat/maxOutputTokens/reasoningLevel），并写 `personalModelIds`/`modelOrder`。
   - 已存在的 LoomX 条目原地更新（即使用户改过名/换过 Key 也能识别收养），用户手加的其他 provider 与排序原样保留。
   - 写入前自动备份。
4. **验证**：重启 ZCode（运行中的客户端缓存 provider 列表，写盘不热更），在模型选择器里确认 LoomX 条目与模型可见，发消息测试。
5. **回滚**：把 `provider_config.json.bak-<时间戳>` 复制回原文件，再次重启 ZCode。

## 注意

- **API Key 落盘**在 provider 条目的 `access.apiKey`；不回显，提醒用户勿外传。
- 模型能力（上下文窗口、视觉、输出上限）来自 LoomX 模型页元数据；未声明的能力不虚构。
- 暴露的模型列表以最近一次写入为准；LoomX 侧 Combo 变化后需重新 `clientconfig.write` 并重启 ZCode。

## 资料获取边界

1. 优先使用模型原生或已有的官方资料能力。
2. 其次使用 Browser Bridge 的 `browser.open`、`browser.read`、`browser.wait` 读取用户授权页面。
3. 遇到登录、CAPTCHA、Cloudflare 或 JS challenge 时立即暂停并交还用户，禁止绕过网站安全机制。
4. 没有可用资料通道时，使用 `assistant.ask_user` 请求用户提供资料或结论。
