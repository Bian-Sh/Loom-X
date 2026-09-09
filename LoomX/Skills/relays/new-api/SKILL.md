# New-API 系中转站接入

## 识别特征

New-API / One-API 及其衍生面板（Sub2API 等）的共同特征：

- 网页控制台有"令牌 / Token / API Key"管理页，Key 通常 `sk-` 开头。
- 对外是 OpenAI 兼容接口：`{站点地址}/v1/chat/completions`、`{站点地址}/v1/models`。
- 渠道（Channel）概念：一个 Key 可限定可用模型分组。

## 标准接入流程

1. 用户提供站点地址与 Key；若让用户在浏览器中操作，走"浏览器自动配置流程"。
2. Base URL = 站点根地址 + `/v1`（例如 `https://relay.example.com/v1`）。
3. 走 generic-openai-compatible 流程：create_provider → test_provider → create_model → create_combo → update_endpoint。
4. 中转站模型名与上游一致（如 `gpt-4o`、`claude-sonnet-4-5`），family 按模型实际系列填写。

## 浏览器自动配置流程（Browser Bridge）

用户说"帮我配置这个中转站"且只给了网址时：

1. `browser.open` 打开站点控制台。只允许自动化标签页，不要碰用户其他标签页。
2. `browser.read` 判断登录态；未登录 / CAPTCHA / 2FA 时发出 WaitingForUser，等用户完成后再继续，不要反复催促。
3. 进入"令牌 / Token"页，`browser.read`（必要时 `browser.click` 新建令牌）获取 Key。
   页面中的 Key 会被自动收割进 Secret Store，你只会看到 `secret_ref`——这是正常且必须的。
4. `browser.network` 查看控制台实际调用的 API：Base URL、`/v1/models` 响应里的模型清单往往比页面文字更可靠。
5. `loomx.create_provider` 用 `api_key_secret_ref` 引用收割的 Key（不要试图拼出明文）。
6. 按标准流程建 Models、Combo 并逐项 test。结束后 `browser.close` 清理现场。

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
   （`Authorization` 会被收割成 secret_ref，看不到明文是正常的。）
2. `loomx.create_provider` / `update_provider` 的 `headers` 参数补齐这些头（例如 `{"User-Agent":"...","X-Client":"..."}`）。
   头值里若含 Secret，会被自动收割，按 secret_ref 引用即可。
3. 重新 `test_provider` / `test_model` 验证。
4. 提醒用户：绕过客户端校验可能违反中转站条款，由其自行权衡；且站点可能随时升级校验导致失效。

## 验证要点

- `test_provider` 的 `models_found` 应与控制台可见模型数量级一致；为 0 多半是 Key 无权限或分组限制。
- 中转站常有倍率/额度限制：`rate_limited` 或 `request_rejected` 时提醒用户查账户额度。
- 部分中转站 `/v1/models` 返回全站模型而非该 Key 可用模型，以 `test_model` 结果为准。

## 安全

- Key 只通过工具参数写入 Secret Store，绝不出现在聊天与日志里。
- 若用户贴了完整 Key 在对话中，提醒该 Key 已暴露，建议在中转站后台重置。
