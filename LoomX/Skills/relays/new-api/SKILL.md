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

## 验证要点

- `test_provider` 的 `models_found` 应与控制台可见模型数量级一致；为 0 多半是 Key 无权限或分组限制。
- 中转站常有倍率/额度限制：`rate_limited` 或 `request_rejected` 时提醒用户查账户额度。
- 部分中转站 `/v1/models` 返回全站模型而非该 Key 可用模型，以 `test_model` 结果为准。

## 安全

- Key 只通过工具参数写入 Secret Store，绝不出现在聊天与日志里。
- 若用户贴了完整 Key 在对话中，提醒该 Key 已暴露，建议在中转站后台重置。
