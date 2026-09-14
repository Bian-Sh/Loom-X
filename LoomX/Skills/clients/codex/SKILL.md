# Codex CLI 接入 LoomX

## 目标

让 Codex CLI 的请求打到 LoomX 的 OpenAI Endpoint，由 LoomX 路由到上游。

## 核心原则：ComboModel + model_catalog_json

LoomX 的 **ComboModel 是对 Codex 暴露的稳定模型身份**，不要把 Provider/实际模型绑定直接写进 Codex 的 model 名称。

Codex 支持通过 `model_catalog_json` 加载自定义 Model Catalog。LoomX 应将 ComboModel 的静态信息投影到该 Catalog：

- `slug`：使用 LoomX ComboModel 的稳定标识/名称。
- `display_name`：使用 ComboModel 的展示名称。
- `context_window`、`max_context_window`、`supported_reasoning_levels`、`input_modalities` 等：由 ComboModel 的能力定义生成。
- Provider、Credential、实际模型、Model Mapping、优先级、故障转移、重试等路由信息 **不进入 Codex Catalog**，全部由 LoomX Router 内部编排。
- 因此同一个 ComboModel 可以绑定多个 Provider/实际模型，并支持故障转移；切换 Provider 或调整路由策略通常不需要修改 Codex Catalog。

典型关系：

```text
Codex
  │
  │ model_catalog_json
  ▼
ComboModel: gpt-5.6-sol
  │
  ▼
LoomX Router
  ├── Provider A / gpt-5.6-sol
  ├── Provider B / gpt-5.6-sol
  └── Provider C / gpt-5.6-sol
       │
       └── failover / retry / mapping / credential
```

## `model_catalog_json` 的重要行为

`model_catalog_json` 是 Codex 的启动时 Model Catalog。当前 Codex 配置源码明确标注为 **applied on startup only**：Catalog 在 Codex 进程启动时加载；运行中的 Codex 不会因为外部 JSON 文件被修改而自动重新读取。

因此：

- **新增/删除/重命名 ComboModel，或修改其对 Codex 可见的能力信息** → 重新生成 Catalog，并重启 Codex 后生效。
- **Provider 增删、Provider 顺序、故障转移、重试、实际模型映射、Credential 等 LoomX Router 内部变化** → 不需要同步修改 Catalog，也不要求为了这些变化重启 Codex。
- 不要把每个 Provider 的模型直接生成为独立 Codex model；应优先保持 ComboModel 身份稳定。

参考：

- Codex 配置参考：`https://developers.openai.com/codex/config-reference/`
- Codex 配置源码：`https://github.com/openai/codex/blob/main/codex-rs/config/src/profile_toml.rs`

## 配置文件

- 路径：`%USERPROFILE%\\.codex\\config.toml`（Windows）/ `~/.codex/config.toml`
- 认证：`%USERPROFILE%\\.codex\\auth.json`（如存在）
- Catalog：由 LoomX 生成并维护的 JSON 文件；实际路径写入 `model_catalog_json`。

## 修改方法（Read → Backup → Patch → Generate Catalog → Validate → Test）

1. **Read**：读取 `config.toml` 现状。
2. **Backup**：复制为 `config.toml.bak-<时间戳>`。
3. **Patch**：将 model_provider 指向 LoomX，并指定 Catalog：

```toml
model = "<LoomX Combo 名称>"
model_provider = "loomx"
model_catalog_json = "<LoomX 生成的 model catalog.json 绝对路径>"

[model_providers.loomx]
name = "LoomX"
base_url = "http://127.0.0.1:<端口>/openai/v1"
env_key = "LOOMX_API_KEY"
wire_api = "responses"
```

- 端口见 `loomx.get_status` 的 `listen_urls`。
- `model` 填 LoomX ComboModel 名称；该名称应存在于 Catalog。
- 上游只支持 Chat Completions 时 `wire_api = "chat"`。

4. **Catalog**：根据 LoomX 当前 ComboModel Registry 生成 `model_catalog_json` 指向的 JSON。Catalog 描述的是稳定的 ComboModel，而不是 Provider 路由表。
5. **Key**：LoomX OpenAI/Azure Endpoint 需要 API Key（在 Endpoint 设置里轮换生成）。把 Key 写入用户环境变量 `LOOMX_API_KEY`；Key 本身不进入聊天上下文。
6. **Validate**：`codex --help` 确认安装；用文本编辑器复査 TOML/JSON 语法，并确认 `model_catalog_json` 文件存在且 ComboModel 的 `slug` 可用。
7. **Test**：运行一次最小 Codex 会话验证连通；失败时先用 `loomx.test_endpoint` 检查 OpenAI Endpoint 是否启用、Combo 是否绑定。
8. **Restart**：如果 Catalog 内容发生变化，重启 Codex，使新的 Model Catalog 被加载。
9. **恢复**：出问题回滚 `config.toml.bak-<时间戳>`。

## 注意

- 不需要重启 LoomX；Codex 重启后读取新的 Catalog。
- `model_catalog_json` 是 Codex 的客户端模型目录，不是 LoomX Router 的路由配置。
- `auth.json` 里如有官方登录态，切换 `model_provider` 后不会被使用，不要删除。
