# 通用 OpenAI 兼容服务接入

## 识别

- 服务提供 `base_url`（通常以 `/v1` 结尾）+ `api_key`（通常 `sk-` 开头）。
- 支持 `GET {base_url}/models` 与 `POST {base_url}/chat/completions`，请求头 `Authorization: Bearer <key>`。

## 接入步骤

1. `loomx.create_provider`：`api_mode=openai`，`base_url` 填服务给的地址（保留 `/v1` 后缀）。
2. `api_key` 通过参数写入（自动 DPAPI 加密，不回显）。
3. `loomx.test_provider` 验证模型列表可达性与鉴权；通过只说明列表可用，具体模型仍需真实测试。
4. `loomx.fetch_provider_models` 只读获取真实 ID 与远端元数据；`loomx.sync_provider_models` 等同 UI 获取按钮，新增/更新模型并保留已有配置。可用 `loomx.set_provider_model_list_url` 设置页面文档给出的自定义模型获取地址，空值恢复默认。仅需少量模型时按真实 ID 创建，不猜上下文规格。
5. Combo/Endpoint 绑定仅在用户要求时修改，先读取并保留已有绑定。
6. `loomx.get_model_api` 确认真实协议、方法和路径，再用 `loomx.test_model` 测试关键模型。

## 注意事项

- `endpoint_format`：上游若只支持 chat completions（大多数兼容服务），设为 `chat_completions`；支持 Responses API 的服务（官方 OpenAI 新接口）可用 `responses`。
- 如果模型列表返回 `endpoint_not_found`，根据真实文档确认 Base URL 与自定义模型列表 URL，不盲目增删 `/v1`。
- `auth_failed` 优先确认 Key 是否复制完整、是否有 IP 白名单。
- 部分兼容服务的 `/models` 不需要鉴权但 chat 需要，`test_provider` 通过不代表模型可用，务必再跑 `test_model`。
- 服务需要代理才能连通时设 `use_proxy: true`；按 User-Agent / 自定义头校验客户端时用 `headers` 补齐——
  具体识别信号与处理流程见 relays/new-api Skill 的"需要代理的中转站"与"卡客户端的中转站"两节。
- 不要把 OpenAI 系（`gpt-*`、`deepseek-*`）与 Claude 系（`claude-*`）模型混挂到同一个 Provider：一个 Provider
  同一时刻只有一种接口兼容模式。混挂后模型会"显示在列表里却调不通"，AI 助手（只走 OpenAI 兼容协议）也会选错。
  排查与告知口径见 loomx Skill 的「诊断要点：一个 Provider 只能有一种接口兼容模式」。
