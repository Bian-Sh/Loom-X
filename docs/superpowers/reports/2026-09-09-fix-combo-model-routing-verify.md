# fix-combo-model-routing 验证报告

## 结论

验证通过。该变更的 5/5 个任务已完成，当前代码位于 `master`，无需额外分支处理。

## 实现对照

- `GatewayEndpointRouting` 先按请求 URL 解析 Endpoint，固定区分 Ollama、OpenAI 和 Azure 入口。
- `GatewayComboCatalog` 仅返回对应 Endpoint 已绑定且启用的 Combo，`GatewayComboMatcher` 只按 Combo 名称匹配，成员 ModelId、显示名和 Ollama 名称不能绕过 Combo。
- 模型列表和请求入口复用同一 Endpoint Combo 目录；Endpoint URI 固定为 `/`、`/openai`、`/azure`，保留 Ollama 的根路径 OpenAI-compatible 操作。
- 回归测试覆盖 Combo 声明、跨 Endpoint 隔离、协议入口和上游 URL 拼接行为。

## 验证项

- tasks.md：5/5 已勾选。
- proposal.md、design.md 与实现目标一致，未发现规格漂移。
- `dotnet build LoomX.slnx --no-restore --nologo`：通过，0 个错误。
- `dotnet test LoomX.slnx --no-restore --no-build --nologo`：298/298 通过，0 失败。
- `openspec validate --specs --strict --no-interactive`：8/8 主规格通过。
- `.design/scripts/validate.ps1`：原型校验通过。
- `git diff --check`：通过。
- 安全检查：未发现本次变更新增的密钥、Authorization 或不安全操作。

## 已知非阻断项

构建保留 7 个既有警告，包括 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的 NU1903 高危漏洞提示，以及既有 nullable/code analysis 警告；本次变更未引入这些警告。
