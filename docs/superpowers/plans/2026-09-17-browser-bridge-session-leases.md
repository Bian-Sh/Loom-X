---
change: fix-browser-bridge-connectivity
design-doc: docs/superpowers/specs/2026-09-17-browser-bridge-session-leases-design.md
base-ref: 76d801b558e26aeecb16bbee42c079a221bc30d4
---

# Browser Bridge Session 租约与 Provider 录入实施计划

> **执行说明：** 本计划由恢复中的 Comet build 阶段补齐；实现采用当前会话直接执行，所有步骤以实际代码、测试和端到端验证为准。

**目标：** 让 LoomX Assistant 以 Session ID 显式共享单例 Browser Bridge，可靠恢复 Chrome Extension，并在服务端安全收割网页 Secret 后完成 Provider 录入。

**架构：** 桌面进程持有可重启的 `BrowserBridge` 与 `BrowserBridgeLeaseManager`；AI 通过 `browser.bridge_start/stop` 管理自己的租约，删除 Session 仅作残留兜底。Extension 通过 WebSocket 心跳与 `chrome.alarms` 重连，Secret 统一在 .NET 边界进入 `BrowserSecretVault`。

**技术栈：** .NET 10、Avalonia、xUnit、`HttpListener`/WebSocket、Chrome MV3 Extension、SQLite、Serilog。

**规格：** `openspec/changes/fix-browser-bridge-connectivity/specs/browser-bridge-lifecycle/spec.md`

## 全局约束

- 正常租约释放只能由 AI 主动调用 `browser.bridge_stop(assistant_session_id)`；切换、离开或关闭 Session UI 不得释放租约。
- 删除 Assistant Session 仅作为意外残留兜底尝试释放该 ID。
- 配置数据库只使用 `%LOCALAPPDATA%\LoomX\LoomX.db`。
- 日志、Toast、Session JSONL 和模型上下文不得出现 API Key、Authorization、请求/响应正文或工具参数。
- Chrome 最低版本为 120；Extension 使用 20 秒 keepalive 与 30 秒 alarm。

---

### Task 1：固化生命周期和安全边界回归测试

**文件：**
- 创建：`LoomX.Tests/Assistant/BrowserBridgeLeaseManagerTests.cs`
- 修改：`LoomX.Tests/Assistant/BrowserBridgeTests.cs`
- 修改：`LoomX.Tests/Assistant/BrowserToolsTests.cs`
- 修改：`LoomX.Tests/Assistant/BrowserSecretHarvesterTests.cs`
- 修改：`LoomX.Tests/Assistant/AssistantServiceTests.cs`
- 修改：`LoomX.Tests/Assistant/LoomXToolsTests.cs`

**接口：**
- 消费：`IBrowserBridgeLifecycle.StartAsync/StopAsync`、`ToolRegistry.InvokeAsync`。
- 产出：租约去重、最后释放停止、停止后重启、Extension 重同步、正文 Secret 收割、Provider `secret_ref` 的可执行回归契约。

- [x] 编写租约管理、Bridge 重启、Session 删除兜底和工具 schema 的失败测试。
- [x] 运行定向测试确认测试在缺少实现时按预期失败。
- [x] 增加遮罩 Key `sk-897...a551` 与 `sk-897…a551` 不得收割的回归测试并确认 RED。
- [x] 在实现后运行 Browser/Assistant 定向测试并确认全部通过。

### Task 2：实现进程级 Session 租约与可重启 Bridge

**文件：**
- 创建：`LoomX/Assistant/Browser/BrowserBridgeLeaseManager.cs`
- 修改：`LoomX/Assistant/Browser/BrowserBridge.cs`
- 删除：`LoomX/Assistant/Browser/BrowserBridgeHost.cs`
- 修改：`LoomX/App.axaml.cs`
- 修改：`LoomX/LoomXHost.cs`
- 修改：`LoomX/Services/GatewayProcessService.cs`

**接口：**
- 产出：`BrowserBridgeLeaseManager.AcquireAsync(string, CancellationToken)`、`ReleaseAsync(string, CancellationToken)`、`ActiveSessionIds`。
- 产出：`BrowserBridge.StartAsync/StopAsync` 幂等生命周期及独立的 `IsListening`/`IsExtensionConnected` 状态。

- [x] 用去重 Session ID 集合实现首租约启动、末租约停止和启动失败回滚。
- [x] 将一次性 Bridge 生命周期改为可停止、可重启并等待后台任务退出。
- [x] 从 Gateway 生命周期移除 Bridge 启停，只在应用退出执行最终释放。
- [x] 运行租约与 Bridge 回环测试确认通过。

### Task 3：向 AI 暴露显式启停工具与 Session 身份

**文件：**
- 修改：`LoomX/Assistant/Browser/BrowserTools.cs`
- 修改：`LoomX/Assistant/AssistantService.cs`
- 修改：`LoomX/ViewModels/AssistantViewModel.cs`
- 修改：`LoomX/Views/AssistantHistoryPanel.axaml.cs`
- 修改：`LoomX/Skills/relays/new-api/SKILL.md`

**接口：**
- 产出：`browser.bridge_start({assistant_session_id})` 与 `browser.bridge_stop({assistant_session_id})`。
- 产出：系统提示中的当前 Assistant Session ID；删除 Session 时异步释放残留租约。

- [x] 注册启停工具并让 start 最多等待 Extension 35 秒自动重连。
- [x] 让 Browser 工具安全错误使用 `ToolResult.SafeFail`，保留可操作错误码。
- [x] 在系统提示和内置 Skill 中约束 AI 使用自身 Session ID 主动启停。
- [x] 验证关闭/切换 Session UI 不释放租约，删除 Session 才触发兜底释放。

### Task 4：增强 Chrome MV3 Extension 恢复能力

**文件：**
- 修改：`LoomX/BrowserExtension/background.js`
- 修改：`LoomX/BrowserExtension/manifest.json`

**接口：**
- 消费：`Bridge.hello`、`Bridge.keepAlive`、Chrome debugger/alarms/tabs API。
- 产出：hello 目标快照、20 秒 keepalive、30 秒 alarm 重连、后台 tab 与 CDP 截图。

- [x] 增加 `alarms` 权限和 Chrome 120 最低版本。
- [x] 实现心跳、快速重试和 alarm 唤醒共用的幂等 `connect()`。
- [x] 断线时保留自动化目标，重新握手上报目标快照。
- [x] 用 `active: false` 创建 tab，并通过 `Page.captureScreenshot` 截图。
- [x] 运行 `node --check` 和 manifest JSON 解析验证。

### Task 5：收敛 Secret 与 Provider 安全边界

**文件：**
- 修改：`LoomX/Assistant/Browser/BrowserSecretVault.cs`
- 修改：`LoomX/Assistant/LoomXTools.cs`
- 修改：`LoomX/Logging/LoggingBootstrap.cs`

**接口：**
- 产出：`BrowserSecretHarvester.Harvest(JsonNode, BrowserSecretVault)` 返回占位符和 `harvested_secrets[].secret_ref`。
- 消费：`loomx.create_provider/update_provider` 的 `api_key_secret_ref`。

- [x] 从 Extension 移除 Secret 识别，把结构化字段和正文收割集中到 .NET。
- [x] 排除包含 `...` 或 `…` 的遮罩 Key，避免把 UI 遮罩值写入 Provider。
- [x] Provider 工具解析 Browser Secret Vault 引用并写入受保护配置。
- [x] 修复 Serilog `shared + buffered` 冲突，保持安全结构化日志。

### Task 6：发布与端到端验收

**文件：**
- 发布：`outputs/2026-09-17-browser-bridge-session-leases-r4/`
- 更新：`openspec/changes/fix-browser-bridge-connectivity/tasks.md`
- 记录：`docs/superpowers/incidents/2026-09-17-assistant-long-response-perceived-freeze.md`

**接口：**
- 消费：Chrome 中已加载的 `LoomX/BrowserExtension`。
- 产出：可运行桌面包、已鉴权的 `loomx` Provider、验证报告。

- [x] 发布独立 r4 桌面包并校验运行进程路径。
- [x] 通过 LoomX Assistant 打开授权页、点击 Loomx 行“使用密钥”、使用真实 `secret_ref` 更新 Provider。
- [x] `loomx.test_provider({"id":"loomx"})` 返回 HTTP 200、`authenticated=true`、8 个模型。
- [x] 关闭自动化标签页并主动释放当前 Session 租约，确认 17831 端口无监听。
- [x] 扫描相关日志和 Session：无未遮罩 `sk-` 明文；数据库仅保存受保护 Key。
- [x] 运行完整 Release 测试、Release build、Extension 语法检查和 CodeGraph 同步。

## 执行与审查记录

- 执行方式：`direct`，因为本变更已经在同一 build 会话内连续实现并具有完整 TDD/端到端证据。
- TDD：遮罩 Key 回归测试先失败后通过；生命周期和工具测试已在实现期间按 RED/GREEN 执行。
- 自动代码审查：`review_mode: off`。跳过原因是用户要求继续现有单会话实现，且本轮采用定向测试、947 项完整测试、Release build、GUI 端到端与 Secret 扫描作为风险控制；未使用自动 reviewer，避免与共享工作区并行修改冲突。
