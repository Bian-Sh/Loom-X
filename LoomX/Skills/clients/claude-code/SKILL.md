# Claude Code 接入 LoomX

## 目标

让 Claude Code 的请求经 LoomX 路由到上游 Anthropic 兼容服务。

## 配置方式

Claude Code 通过环境变量切换网关：

- `ANTHROPIC_BASE_URL`：指向 LoomX 的 openai Endpoint 并带上目标 Combo 的路由（LoomX 会把 Anthropic 协议桥接到上游）。
- `ANTHROPIC_AUTH_TOKEN` 或 `ANTHROPIC_API_KEY`：LoomX openai/azure Endpoint 的 API Key。

## 修改方法（Read → Backup → Patch → Validate → Test）

1. **Read**：检查用户现有环境变量与 `~/.claude/settings.json`。
2. **Backup**：导出当前相关环境变量值；备份 `settings.json`（如存在）。
3. **Patch**：设置用户级环境变量（Windows：`setx`，或写入 settings.json 的 `env` 段）：

```json
{
  "env": {
    "ANTHROPIC_BASE_URL": "http://127.0.0.1:<端口>/openai",
    "ANTHROPIC_AUTH_TOKEN": "<LoomX Endpoint API Key>"
  }
}
```

- 端口见 loomx.get_status 的 listen_urls。
- Combo 选择：确保目标 Combo 已绑定到 openai Endpoint（loomx.update_endpoint），且 Combo 路由的模型支持 anthropic 协议或可由 LoomX 桥接。
4. **Validate**：确认 LoomX 网关在线（loomx.test_endpoint）。
5. **Test**：新终端启动 `claude`，发送一条消息验证；失败先看 loomx.test_provider / test_model 定位上游问题。
6. **恢复**：还原环境变量与 settings.json 备份。

## 注意

- Claude Code 用的是 Anthropic Messages 协议；LoomX 侧需要 Combo 路由到 anthropic 模式的模型，或确认桥接能力可用。
- Key 不进入聊天上下文；已暴露的 Key 建议轮换（Endpoint 设置里支持一键轮换）。
