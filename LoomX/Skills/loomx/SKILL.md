# LoomX 小助手基础知识

## LoomX 是什么

LoomX 是一个纯粹、智能的 AI Router。运行时链路：

```text
Client → Endpoint → Combo → Provider → Model → 上游 AI 服务
```

## 数据模型

- **Endpoint**：对外协议入口，系统预置 `ollama` / `openai` / `azure`，不可创建或删除，只能启停、调整 Reasoning Effort（仅 ollama）与 Combo 绑定。
- **Combo**：对外暴露的"模型名"，内含若干按优先级排序的 Route，每个 Route 指向一个 Model。
- **Provider**：上游 AI 服务（base_url + api_mode + 加密存储的 api_key）。
- **Model**：Provider 下的具体模型，可覆盖 Provider 的 base_url / api_mode / api_key。

## 配置修改原则（必须遵守）

```text
Read → Backup → Patch → Validate → Test → Restart if necessary → Verify
```

- 修改前先用对应的 list/get 工具读取现状。
- 修改后用 loomx.test_provider / loomx.test_model / loomx.test_endpoint 验证。
- 不要直接覆盖配置；删除类操作（Destructive）执行前向用户确认。

## Secret 边界

- 工具输出中的 api_key 永远是 `{"configured": true, "secret_ref": "secret://..."}`。
- 不要试图向用户或上下文中索取、复述已保存的 Key。
- 用户给新 Key 时，直接通过 create/update 工具的 api_key 参数写入，它会被 DPAPI 加密保存，不会回显。

## 常用工具流程

- 查看状态：loomx.get_status → loomx.list_providers / list_combos / list_endpoints
- 新增 Provider：loomx.create_provider → loomx.create_model → loomx.create_combo（挂 model_ids）→ loomx.update_endpoint（绑定 Combo）→ loomx.test_provider / test_model
- 排障：loomx.test_provider → loomx.test_model → 根据 diagnosis（auth_failed / unreachable / timeout / model_not_found 等）解释原因
