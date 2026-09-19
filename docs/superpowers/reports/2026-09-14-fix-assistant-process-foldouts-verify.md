# AI 助手过程 foldout 修复验证报告

## 结论

PASS（含 2 项已记录 WARNING）。最终内容完整输出前，单轮过程父 foldout 保持“处理中”；只有无工具调用的最终 assistant `MessageCompleted` 才切换为“已完成”并默认折叠。思考、工具调用、工具参数与结果的交互以及正文流输出均有自动化测试和本地 Mock 证据。

## 规模判定

Comet 以 `base_ref` 到当前提交统计到 23 个文件，超过完整验证阈值，因此使用 `verify_mode: full`。其中包含一项既有 `AGENTS.md` 改动和本 change 的 Comet/OpenSpec 产物；当前 change 有 3 项任务、0 个 delta spec capability。

## 完整验证记分卡

| 维度 | 结果 | 证据 |
|---|---|---|
| 完整性 | PASS | `tasks.md` 3/3 完成；本 hotfix 不新增 capability，delta spec 按 proposal 明确跳过 |
| 正确性 | PASS | foldout 完成边界、两个子组、完成后固定多语言标签、工具参数/结果、透明交互和正文分段流输出均有测试覆盖 |
| 一致性 | PASS | 实现遵循 `design.md` 的单轮父组、最终 assistant 完成边界、按 `ToolCallId` 关联详情、本地 Mock 隔离等决策 |
| 构建 | PASS | 2026-09-14 执行 `dotnet build LoomX.slnx -c Release --no-restore`：0 错误、1 个既有 `NU1903` 警告 |
| 完整测试 | PASS | 2026-09-14 执行 `dotnet test LoomX.slnx -c Release --no-restore`：599 总数，598 通过，1 跳过，0 失败 |
| 安全检查 | PASS | 产品 diff 未新增 API Key、Authorization、密码、Secret、`Console.WriteLine`、`Debug.WriteLine` 或进程启动逻辑 |
| 代码审查策略 | PASS | Hotfix 预设 `review_mode: off`；按配置跳过自动 reviewer，保留完整测试、构建、发布包和 GUI 验收 |

## 需求与场景映射

- 单轮只生成一个过程父 foldout，最终内容出现前显示“处理中”，最终 assistant 内容完成后显示“已完成”并默认折叠。
- 同一父组只复用“思考”和“工具调用”两个多语言子项；仅处理中且折叠时显示最新内容，完成后固定显示标签。
- foldout 默认、hover、pressed、checked 状态保持透明；箭头放在文字后，仅划入时显示 `#CCFFFFFF`（80% 白色）。
- 工具调用按 `ToolCallId` 展示完整参数和结果；调用与完成/失败详情可独立折叠，内层入口无背景、selected 底色和箭头。
- `Project_TextDeltas_ReuseStreamingMessageUntilFinalMessageCompletes` 覆盖正文分段流式追加；相关 foldout、样式和工具详情测试均在完整测试中通过。

## 本地 Mock 与发布包验收

- 本地 Mock 位于 `.local/assistant-session-mock/`，由 `.git/info/exclude` 排除，不提交。
- Mock 生成 6 个 JSONL 场景，覆盖处理中、已完成、正文四段流输出、切会话来源、切会话目标、失败/拒绝/取消。
- 最新发布目录为 `outputs/20260914-204508/`：406 个文件、1 个 exe；精确检查 `assistant-session-mock` 和 `.local` 路径均为 0，不进入 Release。
- 静态切会话 Mock 验收：来源页仅出现 `SOURCE_ONLY`，切换后的目标页仅出现 `TARGET_ONLY`，没有来源内容污染。证据为 `.local/assistant-session-mock/screenshots/switch-source-running.png` 与 `switch-target-isolated.png`。
- 本轮 GUI 验收启动的 PID 32040、30064 均已退出；复核时不存在来自该发布目录的 LoomX 进程。

## 问题分级

### CRITICAL

无。

### WARNING

- 真实 AI 正文流输出期间切换会话仍有既有生命周期问题：`AssistantService.SendAsync` 的活动记录和持久化读取可变 `CurrentSession`，`AssistantViewModel.Project` 也没有按 `AgentEvent.SessionId` 隔离当前视图。已用跳过测试 `SendAsync_SwitchSessionDuringStreaming_PersistsOriginalRunWithoutPollutingViewedSession` 固化预期，并记录在 `tasks.md` 后续 TODO；本次 foldout hotfix 不混入该会话架构修复。静态 JSONL Mock 的切会话隔离通过不能替代真实运行修复。
- NuGet 报告既有 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 高严重性漏洞公告 `NU1903`；本 change 未修改依赖。

### SUGGESTION

无。

## 流程说明

- Comet build guard 当前不识别 `.slnx/.csproj`；已先显式完成 Release 构建，再使用 `COMET_SKIP_BUILD=1` 跳过重复自动探测，其余 guard 检查照常执行。
- 本 change 使用 `openspec/changes/fix-assistant-process-foldouts/design.md` 记录设计；hotfix 未新增 capability，因此没有独立 delta spec，也没有额外的 `docs/superpowers/specs/` 文档。
- 产品与测试实现提交为 `57e518a`；验证报告和最终 Comet 状态将作为独立收尾提交。
