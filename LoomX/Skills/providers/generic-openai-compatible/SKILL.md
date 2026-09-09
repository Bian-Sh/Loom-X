# 通用 OpenAI 兼容服务接入

## 识别

- 服务提供 `base_url`（通常以 `/v1` 结尾）+ `api_key`（通常 `sk-` 开头）。
- 支持 `GET {base_url}/models` 与 `POST {base_url}/chat/completions`，请求头 `Authorization: Bearer <key>`。

## 接入步骤

1. `loomx.create_provider`：`api_mode=openai`，`base_url` 填服务给的地址（保留 `/v1` 后缀）。
2. `api_key` 通过参数写入（自动 DPAPI 加密，不回显）。
3. `loomx.test_provider` 验证：`diagnosis=provider_ok` 且 `models_found > 0` 即接入成功。
4. 按返回的模型列表逐个 `loomx.create_model`（context_length / max_tokens 按模型实际规格填写）。
5. `loomx.create_combo` 挂接 model_ids，`loomx.update_endpoint` 把 Combo 绑到目标 Endpoint。
6. `loomx.test_model` 抽查关键模型。

## 注意事项

- `endpoint_format`：上游若只支持 chat completions（大多数兼容服务），设为 `chat_completions`；支持 Responses API 的服务（官方 OpenAI 新接口）可用 `responses`。
- 如果 `test_provider` 返回 `endpoint_not_found`，尝试调整 base_url（多一层或少一层 `/v1`）。
- `auth_failed` 优先确认 Key 是否复制完整、是否有 IP 白名单。
- 部分兼容服务的 `/models` 不需要鉴权但 chat 需要，`test_provider` 通过不代表模型可用，务必再跑 `test_model`。
