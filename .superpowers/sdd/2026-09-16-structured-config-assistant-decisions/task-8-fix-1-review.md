# Task 8 修复轮 1 Scoped Re-review

日期：2026-09-17
审查分支：`codex/structured-config-assistant-decisions`
审查基线：`ec20fb39b49cfcbd7be456c0809e510e69b07e06`
代码提交：`164b1e94f0f337fb9a2ad69f09a248c312cb43ec`
修复后提交：`9cd534c4be76341e9a286bd585bbec49b13f8253`
Scoped package：`.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/review-ec20fb3..9cd534c.diff`
约束：仅复核首轮 I1、M1 及修复 diff 新增的 Critical/Important breakage；未修改生产代码、测试、OpenSpec、计划或 Comet 状态。

## 结论

**Approved。**

- I1：**addressed**。
- M1：**addressed**。
- 新增 Critical：无。
- 新增 Important：无。
- 新增 Minor：1 条，为修复报告两行尾随空白导致 `git diff --check` 非零；不影响代码行为或本轮批准结论。

## 首轮 finding 复核

### I1：addressed

#### 运行时策略替换

- `LoomX.Harness/AgentSession.cs:58-73` 新增 `ApplySystemPrompt`：先删除内存中的全部 `ChatRole.System` 消息，再把当前策略插入消息首位，因此不会同时把旧策略和当前策略发送给模型。
- 当历史会话已有 System 消息时，方法复用第一条历史 System 消息的 `Id` 与 `Timestamp`；当不存在 System 消息时才创建新消息。该实现避免以新 ID 追加第二条策略消息，同时保留既有非 System 历史消息顺序。
- `AgentSession.Options` 改为私有 setter，并通过 `Options with { SystemPrompt = systemPrompt }` 与运行时消息同步；`MaxSteps`、`ModelTimeout` 等其他选项不被覆盖。
- `LoomX/Assistant/AssistantService.cs:120-126` 在 `AssistantSessionStore.LoadAsync` 恢复成功后、赋值 `CurrentSession` 前调用 `session.ApplySystemPrompt(SystemPrompt)`。不存在的会话仍直接返回 `false`，不会改变当前会话。
- `LoomX.Harness/AgentLoop.cs:75-78` 仍直接以 `session.Messages` 构造真实 `ModelRequest`，所以经过上述规范化后，实际请求唯一使用当前 `AssistantService.SystemPrompt`，旧持久化策略不再生效。

#### 重复消息、会话 ID、持久化与切换语义

- `AgentSession.RestoreId` 未改动，加载对象仍沿用持久化 `session_id`；现有 `LoadSession_RestoresHistory_AsCurrentSession` 验证加载后的当前会话 ID 与原 ID 一致。
- 对已有 System 消息复用 ID，与 `AssistantSessionStore.SaveAsync` 的已知 ID 去重逻辑一致：继续发送保存时不会把替换后的策略作为第二条 System 消息追加。旧 jsonl 行不被破坏性重写，但以后每次通过 `AssistantService.LoadSessionAsync` 恢复都会在模型请求前重新应用当前策略。
- 非 System 消息未删除或改写；现有 `SendAsync_SwitchSessionDuringStreaming_PersistsOriginalRunWithoutPollutingViewedSession` 继续通过，说明加载/切换期间运行会话仍保存到原会话，当前查看会话不会被污染。
- `AssistantSessionStoreTests` 随定向集合通过，未发现追加式持久化、恢复或会话标识行为回归。

#### 真实请求回归测试

- `LoomX.Tests/Assistant/AssistantServiceTests.cs:148-184` 从磁盘保存并加载带旧策略的会话，继续调用 `SendAsync`，然后直接检查 `ScriptedModelClient.Requests` 捕获的 `ModelRequest.Messages`。
- 测试断言 System 消息恰好一条，且资料通道顺序为“模型原生/官方能力 → Browser Bridge → assistant.ask_user”；同时覆盖 Cloudflare、JS challenge、立即交还用户、禁止绕过安全机制，并断言旧提示不存在。
- 该测试检查的是实际模型请求，不是仅检查 `Options.SystemPrompt` 或服务常量，能够回归首轮缺陷路径。

综上，I1 已完整修复；未发现该修复对会话 ID、历史消息、追加持久化或会话切换语义造成新的 Critical/Important breakage。

### M1：addressed

#### 新发布目录与版本追溯

- 新目录 `outputs/2026-09-17-1601-structured-config-assistant-decisions` 实际存在，`LoomX.dll` 与 `LoomX.exe` 的创建时间均为 2026-09-17 16:01 左右。
- 现场读取到两者的 `ProductVersion` 均为 `0.12.6+164b1e94f0f337fb9a2ad69f09a248c312cb43ec`，与本轮生产代码提交完整 SHA 一致。`9cd534c` 只追加修复报告/流程证据，不包含后续生产代码变化，因此产物与代码 HEAD 的追溯关系成立。
- 修复报告 `task-8-fix-1-report.md:91-116` 记录了干净代码 HEAD、全新输出目录、完整 publish 命令和退出码。

#### 同 PID Path 与日志

- 修复报告记录以 `--allow-multiple-instances` 启动 PID `43440`，等待 8 秒后该 PID 仍存活，且 `Process.Path` 精确等于新发布目录中的 `LoomX.exe`（`task-8-fix-1-report.md:128-143`）。
- 实际日志 `%LOCALAPPDATA%\LoomX\logs\loomx-20260917.log` 可复核：
  - 第 8558 行为 PID `43440` 的“调试启动已允许多个桌面实例”；
  - 第 8559 行为同一 PID 的“桌面应用启动”，进程路径、基目录、启动工作目录和规范化工作目录均指向本轮新输出目录；
  - 后续同一 PID 有配置服务创建、Provider 页面刷新和概览刷新完成记录；
  - 未发现同一 PID 的“检测到已有实例”、bootstrap 失败或自启动子进程失败记录。
- `InstanceLaunchPolicyTests` 3/3 通过；代码中 `allowMultipleInstances` 的判断位于 shell bootstrap 与单实例 mutex 分支之前。以上证据足以区分实际应用进程与首轮误记的短生命周期 bootstrap 进程。

#### 仅停止本轮 PID

- 修复报告先记录既有 PID `35476` 的 Path/StartTime，再明确只执行 `Stop-Process -Id 43440`，并记录本轮 PID 消失、既有 PID `35476` 在验证结束时仍存活且 Path 不变（`task-8-fix-1-report.md:118-126,161-174`）。
- 当前复核时 PID `43440` 已不存在；PID `35476` 也已在之后退出，因此当前进程表不能重放 16:01 时刻的最终快照，但这不与报告中的当时结果冲突。发布目录、程序集版本以及 PID `43440` 的同 PID 启动日志均可独立核验，报告中的启动/停止叙述不存在首轮那种 PID 与日志相互矛盾的问题。

综上，M1 的发布版本、实际应用 PID、路径、初始化日志和限定停止范围证据已补齐，首轮证据缺口已修复。

## 新 Findings

### Critical

无。

### Important

无。

### Minor

#### [M2] 修复报告的两行 Markdown 尾随空白使 scoped `git diff --check` 失败

- **位置**：`.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-8-fix-1-report.md:3-4`
- **影响**：`git diff --check ec20fb39b49cfcbd7be456c0809e510e69b07e06 9cd534c4be76341e9a286bd585bbec49b13f8253` 返回非零；若集成门禁要求 diff-check clean，会造成补丁卫生检查失败。该问题仅位于报告 Markdown，不影响生产行为、测试或发布证据。
- **证据**：命令准确报告第 3、4 行 `trailing whitespace`。
- **建议**：后续流程性整理时删除两行末尾的两个空格；无需进行无关全仓格式化。

## 验证记录

1. `git apply --check --reverse -- .superpowers/sdd/2026-09-16-structured-config-assistant-decisions/review-ec20fb3..9cd534c.diff`
   - 退出码 0，scoped package 可完整反向匹配当前修复后工作树。
2. `dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantServiceTests"`
   - 退出码 0；失败 0，通过 23，跳过 0。
3. `dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~InstanceLaunchPolicyTests"`
   - 退出码 0；失败 0，通过 3，跳过 0。
4. Task 8 扩展定向集合（含 `AssistantSessionStoreTests` 与 `InstanceLaunchPolicyTests`）
   - 退出码 0；失败 0，通过 245，跳过 0。
5. `git diff --check ec20fb39b49cfcbd7be456c0809e510e69b07e06 9cd534c4be76341e9a286bd585bbec49b13f8253`
   - 退出码 1；仅报告本报告 M2 所述的两处 Markdown 尾随空白。
6. 测试首次并行启动时，一条 `AssistantServiceTests` 命令因另一条测试同时占用 `LoomX.Harness.dll` 输出文件而触发 `CS2012`；改为串行复跑后 23/23 通过。该现象是审查命令并发写同一 `obj` 目录造成的构建锁冲突，不是产品测试失败。

测试输出仅包含仓库既有 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的 NU1903 警告；未据此要求无关依赖或全仓格式化变更。