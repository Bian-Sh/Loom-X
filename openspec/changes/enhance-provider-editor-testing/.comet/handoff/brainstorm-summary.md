# Brainstorm Summary

- Change: enhance-provider-editor-testing
- Date: 2026-09-18

## 确认的技术方案

将 Provider 编辑器收敛为“基础 / 高级 / 模型 / 测试”四个 Tab。基础 Tab 隐藏内部 Provider ID，并以三个兼容类型卡片映射现有 `ApiMode` 与 `EndpointFormat`；API Key 移入基础 Tab。高级 Tab 仅保留代理、自定义请求头和 CLI/UA 身份模拟。新增独立的 `ProviderTestPanelViewModel` 与 `ProviderTestService`，测试请求从当前编辑中的 Provider 快照读取模型、协议、密钥、Header、代理和 CLI 身份，通过现有 `IProviderExecutionPipeline` 发送普通或流式请求并统一呈现安全摘要、结果和错误。

测试服务按 OpenAI Chat Completions、OpenAI Responses、Anthropic Messages 三种协议构造最小请求，使用与生产链路一致的 URL 语义、鉴权和流式事件解析规则。测试状态不持久化；切换 Provider 时取消旧请求并清空响应。UI 采用请求表单、配置摘要和终端式 Response 面板，响应正文仅在用户界面展示并限制最大长度。

## 关键取舍与风险

- 保留现有数据库字段，不新增 schema；兼容类型只是 ViewModel 映射层，避免迁移风险。
- Provider ID 在新建时生成并保持稳定，已有 ID 不变；唯一性仍由配置服务兜底。
- 测试器复用发送管线，但不直接复用助手完整客户端，避免引入工具调用和会话状态；协议解析仅覆盖文本测试所需的最小集合。
- 代理设置读取全局配置，Provider 的 `UseProxy` 决定本次测试是否启用；代理密码仅在内存使用且不进入日志。
- 流式追加需要节流和总长度上限，避免高频 UI 更新及异常响应造成卡顿。
- 旧 `ollama` Provider 仅在兼容类型回显时回退到 OpenAI Chat；保存后进入受支持组合，不增加第四种新卡片。

## 测试策略

先以失败测试固定兼容类型映射、自动 ID、三协议请求体/鉴权/路径、普通与流式解析、截断、取消、代理与安全日志，再实现服务与 ViewModel。随后增加 ProvidersView XAML 契约测试、本地化覆盖测试和完整构建。最后使用 CUA 后台验证四个 Tab、发送/停止/重试/复制/清空、普通/流式响应和错误态，并重新发布到带可读时间的 `outputs` 目录。

## Spec Patch

无。现有 delta spec 已覆盖确认的范围、边界条件和安全约束。
