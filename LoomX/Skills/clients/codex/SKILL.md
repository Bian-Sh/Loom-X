# Codex CLI 接入 LoomX

## 目标

让 Codex CLI 的请求打到 LoomX 的 openai Endpoint，由 LoomX 路由到上游。

## 配置文件

- 路径：`%USERPROFILE%\.codex\config.toml`（Windows）/ `~/.codex/config.toml`
- 认证：`%USERPROFILE%\.codex\auth.json`（如存在）

## 修改方法（Read → Backup → Patch → Validate → Test）

1. **Read**：读取 `config.toml` 现状。
2. **Backup**：复制为 `config.toml.bak-<时间戳>`。
3. **Patch**：将 model_provider 指向自定义 provider：

```toml
model = "<LoomX Combo 名称>"
model_provider = "loomx"

[model_providers.loomx]
name = "LoomX"
base_url = "http://127.0.0.1:<端口>/openai/v1"
env_key = "LOOMX_API_KEY"
wire_api = "responses"
```

- 端口见 loomx.get_status 的 listen_urls；model 填 Combo 名。
- 上游只支持 chat completions 时 `wire_api = "chat"`。
4. **Key**：LoomX openai/azure Endpoint 需要 API Key（在 Endpoint 设置里轮换生成）。把 Key 写入用户环境变量 `LOOMX_API_KEY`；Key 本身不进入聊天上下文。
5. **Validate**：`codex --help` 确认安装；用文本编辑器复査 toml 语法。
6. **Test**：运行一次最小 codex 会话验证连通；失败时先用 loomx.test_endpoint 检查 openai Endpoint 是否启用、Combo 是否绑定。
7. **恢复**：出问题回滚 `config.toml.bak-<时间戳>`。

## 注意

- 不需要重启 LoomX；Codex 重启后生效。
- auth.json 里如有官方登录态，切换 model_provider 后不会被使用，不要删除。
