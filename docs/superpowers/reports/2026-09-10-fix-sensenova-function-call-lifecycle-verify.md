# SenseNova Function Call 生命周期修复验证报告

## 结论

PASS。真实会话中的卡顿根因已消除：SenseNova 参数增量帧里的空 `finish_reason` 不再触发提前完成，工具参数在进入 AgentLoop 前必须组装为单个有效 JSON object；同 id 的完整参数快照不会再拼成 `{}{}...`。

## 变更规模

- OpenSpec 任务：3/3 完成
- delta spec：0
- 业务改动：`OpenAiCompatibleModelClient.cs` 与对应测试文件，共 2 个文件
- Comet 自动统计把 change 内部状态文件计入 13 个变更文件，因此误判为 full；本次按实际业务范围覆盖为 light 验证

## 验证证据

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| 真实故障复现 | PASS | 修复前回归测试稳定得到参数仅为 `{`，重复快照得到 `{}{}...` |
| 参数流解析 | PASS | 覆盖空 `finish_reason`、三组并行参数分片、同 id 扇出与重复完整快照 |
| 无效参数阻断 | PASS | 不完整 JSON 在模型客户端层抛出安全错误，不进入工具执行与会话历史 |
| 自动生命周期 | PASS | 客户端首轮工具调用 → AgentLoop 执行 → tool 结果回传 → 第二轮最终回答 → Session Completed |
| SenseNova 真实验收 | PASS | 使用本机只读 `LoomX.db` 与 DPAPI 配置运行 `sensenova/glm-5.2`；代码限制为 2 次请求、两次间隔至少 5 秒，测试 8 秒通过 |
| Assistant 测试 | PASS | 207/207 通过 |
| 全量测试 | PASS | 531/531 通过 |
| 格式检查 | PASS | `dotnet format ... --verify-no-changes` 通过 |
| Release 构建 | PASS | 0 错误，6 个既有警告 |
| 桌面发布 | PASS | `outputs/20260910-225818/`，唯一应用入口为 `LoomX.exe` |
| 安全检查 | PASS | 无硬编码 API Key；真实测试只读统一数据库路径，不输出请求正文、响应正文或工具参数 |
| 代码审查 | 跳过 | hotfix 配置为 `review_mode: off`；已手工核对正确性、安全与边界条件 |

## 已知非阻塞项

- `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 的 NU1903 漏洞警告为仓库既有依赖问题。
- `SettingsViewModel` 的 CS8618、`AnthropicResponseMapper` 的 CA2024 与两处测试 CS8602 为既有警告，本次未改动对应文件。
- Superpowers `verification-before-completion` 与 `finishing-a-development-branch` 技能未安装；本报告按其要求等价执行了新鲜测试、构建、发布、安全检查和分支推送。

## 分支状态

- 分支：`master`
- 实现提交：`3f422a8`、`4b220c8`
- 已推送到 `origin/master`

