# Hermes Agent 接入 LoomX

## 目标

把 Hermes Agent 的自定义 Provider 注册到 LoomX 网关（chat_completions 协议）。

## 标准流程（clientconfig.* 工具）

1. **检查现状**：`clientconfig.inspect { "client": "hermes" }` → missing / unmanaged / managed / invalid。
2. **确认前置**：`loomx.list_endpoints` 确认 openai Endpoint 已启用并绑定 Combo。Hermes 无法自行发现模型，必须携带模型列表。
3. **写入**：`clientconfig.write { "client": "hermes" }`
   - 工具写 `~/.hermes/config.yaml`：`custom_providers` 序列里的 `loomx` 条目（base_url 带 `/v1`、**内联 api_key**、api_mode=chat_completions、单数 model + 复数 models 元数据），并改写 `model:` 段的 provider/base_url/api_mode/default。
   - api_key 必须内联：Hermes 否则按「.env 或按主机名派生的环境变量」找凭据，回环地址匹配不到任何主机，必然报缺 Key。
   - 只重写 owned 的两个 section，其余内容（含注释）逐字节保留；替换后重新解析验证。写入前自动备份。
4. **验证**：新会话启动 hermes，`/model` 里确认 loomx provider，发消息测试。
5. **回滚**：把 `config.yaml.bak-<时间戳>` 复制回原文件。

## 注意

- **API Key 落盘**在 `custom_providers[].api_key`；不回显，提醒用户勿外传。
- `HERMES_HOME` 环境变量为绝对路径时，配置在 `$HERMES_HOME/config.yaml`。
- 模型列表以最近一次写入为准；LoomX 侧 Combo 变化后重新写入。

## 资料获取边界

1. 优先使用模型原生或已有的官方资料能力。
2. 其次使用 Browser Bridge 的 `browser.open`、`browser.read`、`browser.wait` 读取用户授权页面。
3. 遇到登录、CAPTCHA、Cloudflare 或 JS challenge 时立即暂停并交还用户，禁止绕过网站安全机制。
4. 没有可用资料通道时，使用 `assistant.ask_user` 请求用户提供资料或结论。
