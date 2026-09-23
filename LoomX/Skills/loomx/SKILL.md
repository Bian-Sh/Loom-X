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

## 诊断要点：一个 Provider 只能有一种接口兼容模式

- `api_mode` 决定 Provider 对外呈现的接口协议（`openai` / `anthropic`），**同一时刻只有一种模式生效**。
- 已知陷阱：存储层允许多值写法（如 `openai;anthropic`）。一旦写成多值，所有模型都会被判定为两端可达，
  原本互斥的模型便混在一起——OpenAI 系 ID（`gpt-*`、`deepseek-*`）与 Claude 系 ID（`claude-*`）同时挂在同一个 Provider 下。
- 排查信号：`loomx.list_providers` 或模型列表中，同一个 Provider 同时出现 OpenAI 系与 Claude 系模型 ID。
- 发现后**不要当作正常状态略过**，必须明确向用户说明这三点：
  1. 该 Provider 的 `api_mode` 是混合值，需要收敛成单一模式；
  2. AI 助手只走 OpenAI 兼容协议，设为 Claude Messages 模式的 Provider，其模型不会出现在助手的模型选择列表中；
  3. 建议按协议拆成两个 Provider（一个 openai、一个 anthropic），或将该 Provider 收敛为目标模式后复查。
- 反例：不要仅凭某次 `test_model` 通过就判定配置合理。混合模式下部分模型是"侥幸命中"当前路径工作，
  一旦场景切换（改用助手、改走网关另一条 Route）就会失效。

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
- 连接被拒（Connection refused / 主机积极拒绝）：先看 loomx.get_status 的 `gateway` 字段。`running: false` 说明网关未启动（配置层完全正常也会被拒），提示用户在概览页点"启动网关"；`running: true` 才继续排查端口/防火墙/跨机访问。
- 上游排障：loomx.test_provider → loomx.test_model → 根据 diagnosis（auth_failed / unreachable / timeout / model_not_found 等）解释原因
