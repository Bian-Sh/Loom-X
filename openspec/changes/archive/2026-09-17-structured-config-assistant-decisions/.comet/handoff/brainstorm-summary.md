# Brainstorm Summary

- Change: structured-config-assistant-decisions
- Date: 2026-09-16

## 确认的技术方案

本变更采用完整架构路径，先交付与具体 Client 无关的两项基础能力，再由后续 `codex-client-integration` 复用：

1. 引入基于 Tomlyn 2.10.1 语法树的 `TomlDocumentService`，以 `string[]` 表示无歧义路径，支持 read/get/validate/set/delete/patch。所有写操作先构建内存候选文档，验证通过后执行同目录备份、临时文件写入和原子替换；候选内容未变化时返回 no-op，不生成备份。
2. 新增独立 `UserDecisionBroker` 与 `assistant.ask_user` 工具。Broker 使用 request id 与 `TaskCompletionSource` 暂停当前工具调用，通过事件把 pending request 交给桌面端；提交、取消、会话停止或页面卸载都会完成等待，防止永久阻塞。
3. TOML 工具与 AskUser 都接入现有 `ToolRegistry`、`AssistantService`、`AssistantViewModel`、`GlassDialogWindow`、`ToastService` 和 `ILogger<T>`，不改变 `ToolDefinition` 契约，不引入 WebView、第三方 Agent Runtime 或搜索 Provider。
4. 资料获取保持“模型已有能力 → Browser Bridge → AskUser”顺序；登录、验证码、Cloudflare/JS challenge 由用户处理，不实现绕过。

## 关键取舍与风险

- 语法树写回可能改变局部空白，但必须保留注释、未知字段、无关 section 与语义；结果通过 `formatting_changed` 安全摘要如实报告。
- Windows 文件占用可能导致替换失败，因此临时文件与备份固定在目标目录，并采用有限重试；失败时保留原文件与可恢复备份。
- AskUser 与工具审批语义不同，使用独立 Broker，避免把业务选择错误映射成批准/拒绝。
- 所有输出与日志执行统一敏感键识别，不记录完整 TOML、Secret、Authorization、用户输入正文或工具参数。
- 本变更不包含 Codex Catalog、Codex `config.toml`、环境变量写入或 Codex 重启；这些在依赖变更中实现。

## 测试策略

- 先以 TDD 覆盖 TOML 值契约、路径解析、敏感键识别、读取/验证、结构化 Patch、注释与未知字段保留、no-op、备份、原子替换、Windows 非 ASCII 路径、文件占用和失败回滚。
- 覆盖六个 `toml.*` 工具的名称、Schema、风险等级、取消、输入限制与脱敏输出。
- 覆盖 AskUser 模型校验、并发 request id、提交、取消、超时、会话取消、重复完成、页面卸载和敏感内容过滤。
- 覆盖 `AssistantService`/AgentLoop 恢复流程、`AssistantViewModel` pending 状态、Dialog 字段组合与 Toast 安全摘要。
- 最后运行完整 `dotnet test`、OpenSpec strict validate，并重新发布 standalone 应用到带可读时间的 `outputs/` 目录。

## Spec Patch

无。现有 delta spec 已覆盖验收场景、边界条件与非目标。
