# New-API 系中转站接入

## 适用范围与入口

用户只需提供站点链接和接入目标，例如“把这个站点接入 LoomX”。密钥分支、命名、验证与收尾由本 Skill 编排，不要求用户提供工具步骤或提示词。未知面板先读取真实页面识别；New-API、One-API、Sub2API 等面板通常有令牌或 API 密钥管理和 OpenAI 兼容接口，但常见路径只是线索，实际地址和协议必须以页面文档为准。

## 接入前检查

1. 能力不明确时先用 `loomx.get_capabilities` 查询 provider/model/browser 类别；参数错误时指定 `tool_name` 查看当前完整 schema，不重复猜参数。调用 `loomx.list_providers` 读取现状，避免重复新增或误改已有配置；完整接入资料已给出时直接走配置验证，不启动浏览器。
2. 调用 `loomx.test_url` 验证用户链接可达性。HTTP 可达与鉴权成功、模型可用是不同结论。
3. 已加载本 Skill 后仍需由 AI 判断链接与新增 Provider 意图，确实缺少页面资料才走浏览器流程。

## Browser Bridge Session 租约

- 是否启动由 AI 根据用户消息与上下文判断：只有用户提供了链接并希望新增 Provider，且确实需要读取页面时才调用 `browser.bridge_start`。新建或恢复会话、普通对话、仅提供链接、查看或修改已有 Provider、加载本 Skill 都不得触发连接。
- 使用系统提示中的“当前 Assistant Session ID”作为 `assistant_session_id`，不得传入其他 Session ID。
- 浏览器操作结束时主动调用 `browser.close` 关闭本流程新建的自动化页，再调用 `browser.bridge_stop` 释放同一 Session 租约。
- 新建、切换、离开或关闭 Session UI 不会自动释放租约。删除 Session 仅清理意外残留；失败或取消的宿主兜底不能替代 AI 主动收尾。多个 Session 可共享 Bridge，不释放其他 Session。

## 从真实页面读取资料

1. `browser.open` 打开授权控制台，保存其 `target_id` 和 `session_id`，后续按工具 schema 分别传入，不混用。
2. `browser.wait` 后调用 `browser.read`。遇到登录、CAPTCHA、2FA、Cloudflare 或 JS challenge，暂停交还用户，不尝试绕过。
3. 从 `links`/`controls` 的真实 `href`、`selector` 找密钥页和文档；必要时按 selector 读 HTML。禁止猜测 `/api-keys`、CSS 选择器、API Base URL 或协议。每次导航、弹窗或提交之后重新读取控件，旧 selector 不保证仍有效。
4. 点击找不到控件时重新 `browser.read`，核对当前页面与 selector；不要用截图 base64 猜测页面。截图当前只是编码字符串，并不等于模型已经获得图像视觉输入。不要连续截图或重复失败参数。
5. `browser.network` 只提供实际请求方法、地址、状态与 Header 线索，不包含响应正文；不得声称从该工具读到了模型列表或新建密钥响应。控制台内部 API 不等于推理 API。

## 密钥选择与创建

先在真实密钥页读取全部名称；若有分页，按实际下一页控件逐页读取全部真实名称。不要在选择前创建、重置或删除密钥，也不要只列第一项。

已有密钥时用一个 `purpose="credential_name"` 的 `single_select` 字段列出全部真实名称，选项 ID 为稳定非敏感标识，不得使用密钥值；设置 `allow_custom_input=true`、`custom_input_placeholder="输入新密钥名称"`、`max_length=100`。问题明确：选已有项表示复用；自定义名称非空表示创建新密钥，提交后按该名称创建并继续接入。选择和自由输入分别保留在 `values` 与 `custom_inputs`；自定义名称非空按明确约定创建，否则复用所选项，不替用户清空任何字段。不要新增独立 text 字段，避免分成两个页面。

没有密钥时直接请求名称，设置 `purpose="credential_name"` 和 `default_text="LoomX"`，使用下面的结构，不询问是/否：

```json
{
  "title": "创建 API 密钥",
  "question": "输入密钥名称；提交后创建该名称的密钥并继续接入。",
  "impact_summary": "将在当前站点创建持久 API 访问凭据。",
  "fields": [{
    "id": "key_name",
    "label": "密钥名称",
    "type": "text",
    "purpose": "credential_name",
    "is_required": true,
    "default_text": "LoomX",
    "max_length": 100
  }],
  "allow_cancel": true
}
```

名称与凭据值是不同数据。名称字段必须声明 `purpose="credential_name"`，用于列出真实名称或取名，不能用于索取密钥值。默认 general 会保守拦截英文 Key/Token 等词，不要因校验失败改成无意义的 Q/a/b 或是/否测试；先核对当前 schema 与字段用途。用户可直接提交默认值 `LoomX` 或自定义。提交即确认创建，不追加二元确认；取消则停止创建。根据真实创建表单填写名称、采用页面可选的最小权限，提交后重新读取成功提示和列表，不把打开弹窗当成创建成功。失败先读取错误摘要，禁止反复提交造成重复密钥。

已有密钥仅使用页面允许查看或复制的值；只显示掩码时不能猜测或重置。凭据由统一 Credential Protection 在 Provider 请求边界转成长期本地引用；`api_key` 传完整引用并保持原样，不使用不存在的 `api_key_secret_ref`，不向用户索要明文，不把凭据写入回复或日志。无法取得凭据时明确缺少的页面能力，不能假装已经接入。

## 配置与完整验证

1. 从真实文档获取 Base URL、模型列表地址与协议。常见 `/v1`、`/models`、`/chat/completions`、`/responses` 只能作为线索，不据此盲目拼接。
2. 加载 `providers/generic-openai-compatible`；调用 `loomx.create_provider` 使用当前 schema 的 `business_id`、`display_name`、`base_url`、`api_mode`、`endpoint_format` 和 `api_key`，再调用 `loomx.test_provider`。
3. `test_provider` 的 `models` 是实际模型 ID；`loomx.fetch_provider_models` 可只读获取完整元数据；页面提供自定义模型列表 URL 时用 `loomx.set_provider_model_list_url` 只修改该字段，空值恢复默认。用户希望导入模型时调用 `loomx.sync_provider_models`，等同模型页获取按钮，保留已有模型与配置；少量指定模型也可按真实 ID 创建。不得把来源猜成模型接口、额度或上下文规格。
4. 调用 `loomx.get_model_api` 确认具体模型实际协议、HTTP 方法、主机、路径与代理状态，再用 `loomx.test_model` 发送最小真实请求。Provider 列表通过不代表具体模型通过；200 但响应结构无效也不是成功。
5. Combo 或 Endpoint 绑定只有在用户要求时修改，读取并保留已有绑定；仅新增 Provider 时不要自动替换网关配置。
6. 无论成功或可恢复失败，关闭本流程新建的自动化页并释放自己的 Bridge 租约。回复只列安全配置摘要、可达性/模型测试结果和实际限制，不宣称未完成的步骤成功。

## 需要代理的中转站

部分中转站只对特定地区/网络开放，或有严格的 WAF，直连会超时、RST 或返回 403。

识别信号：
- `test_provider` 返回 `tcp_connect_failed` / `tcp_timeout` / `tls_timeout`，但用户浏览器能正常打开站点。
- `loomx.diagnose` 的分层结果里 DNS ok、TCP/TLS 失败，或 TLS 握手中途被重置。

处理流程：
1. 先确认用户本机已有代理（浏览器能开站即说明有可用通道）。
2. `loomx.create_provider` / `update_provider` 时设置 `use_proxy: true`，让该 Provider 的请求走系统代理。
3. 重新 `test_provider` 验证；通过后再建 Models / Combo。
4. 向用户说明：该 Provider 已标记走代理，本机代理关闭时它会不可用，这是预期行为。

注意：浏览器自动配置流程里 `browser.*` 走的是用户 Chrome（天然带系统代理），所以网页探测能通不代表 API 直连能通——**必须以 test_provider 为准**，不通就回来补 `use_proxy`。

## 卡客户端的中转站

部分中转站会按客户端特征拦截请求：校验 User-Agent、要求特定自定义头、或只放行自家客户端/浏览器。

识别信号：
- `test_provider` / `test_model` 返回 401/403 但 Key 确认有效（浏览器控制台里同一 Key 的 API 调用是 200）。
- 响应体或站点公告里出现 "client not supported"、"请使用官方客户端" 之类字样。

处理流程：
1. 用 `browser.network` 抓取站点控制台（或官方客户端）真实的 API 请求，对比其中的 `User-Agent` 与自定义头。
   （`Authorization` 由 Credential Protection 统一处理，不要索取明文。）
2. `loomx.create_provider` / `update_provider` 的 `headers` 参数补齐这些头（例如 `{"User-Agent":"...","X-Client":"..."}`）。
   头值里若含 Secret，会被自动收割，按完整凭据引用原样传入工具即可。
3. 重新 `test_provider` / `test_model` 验证。
4. 提醒用户：绕过客户端校验可能违反中转站条款，由其自行权衡；且站点可能随时升级校验导致失效。

## 验证要点

- `test_provider` 的 `models_found` 应与控制台可见模型数量级一致；为 0 多半是 Key 无权限或分组限制。
- 中转站常有倍率/额度限制：`rate_limited` 或 `request_rejected` 时提醒用户查账户额度。
- 部分中转站 `/v1/models` 返回全站模型而非该 Key 可用模型，以 `test_model` 结果为准。

## 安全

- Key 只通过工具参数写入 Secret Store，绝不出现在聊天与日志里。
- 若用户贴了完整 Key 在对话中，提醒该 Key 已暴露，建议在中转站后台重置。

## 可达性与具体模型接口测试

- 仅验证用户提供的网址可达性时调用 `loomx.test_url`，不进入 Browser Bridge 流程。`reachable` 表示收到HTTP响应；401/403也说明网络已到达，不能把它说成模型测试成功。
- `loomx.test_provider` 的 `models` 返回实际模型ID，按这些ID配置模型，不从模型数量猜测名称。
- 配置后用 `loomx.get_model_api` 查看具体模型的协议、方法、主机与请求路径，再用 `loomx.test_model` 发起真实请求。根据页面/网络证据设置 Provider 的 `endpoint_format`，不要把 Responses 与 Chat Completions 混用。
- 工具调用历史可能隐藏参数，每次按当前工具schema重建参数；不要复制 `summary` 或隐藏摘要。参数错误先检查schema与实际工具结果，不要反复重试同样错误。
