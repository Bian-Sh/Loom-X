# AI 助手决策订阅生命周期验证报告

- 日期：2026-09-20
- Change：`fix-assistant-decision-subscription-lifecycle`
- 结论：PASS

## 规格线索与根因

- `openspec/specs/assistant-user-decisions/spec.md` 将 AskUser 限定为高影响配置决策，并明确普通内部步骤不应额外询问。
- `openspec/changes/archive/2026-09-17-structured-config-assistant-decisions/tasks.md` 只要求 ViewModel 处理 pending request 与页面卸载，不要求页面挂载即订阅。
- `openspec/changes/fix-browser-bridge-connectivity/specs/browser-bridge-lifecycle/spec.md` 规定由 AI 判断任务需要 Browser Bridge 后申请 Session 租约。
- 根因是 `AssistantView.AttachedToVisualTree` 和 DataContext 挂载路径调用 `AssistantViewModel.Activate()`，同时 `EnsureServiceAsync()` 在模型选择等通用初始化路径隐式尝试订阅，使导航行为早于用户请求激活 Broker。

## 完整性

- `tasks.md`：3/3 完成。
- Delta spec：`assistant-user-decisions` 已补充页面导航、用户请求开始和请求结束场景。
- 实现、测试、规格与发布产物均已生成。

## 正确性

- `AssistantView` 挂载与 DataContext 切换不再调用 `Activate()`；导航不会产生订阅。
- `AssistantViewModel.EnsureServiceAsync()` 不再包含订阅副作用；打开模型选择器、历史等非发送路径不会激活决策通道。
- `AssistantViewModel.SendAsync()` 在服务就绪后、AgentLoop 开始前激活订阅，并在 `finally` 中停用；失败、取消和正常完成路径统一收敛。
- 页面卸载仍调用幂等 `Deactivate()`，可取消已领取的 pending request。
- Skill/Browser Bridge 契约未改动：Provider/中转站由模型按系统提示加载 Skill，Bridge 仍由 AI 按 Skill 和 Session 租约规则调用。

## 场景覆盖

- 页面挂载不订阅：`AssistantDecisionLifecycleTests.AssistantView_挂载不订阅且活动请求在卸载时取消已Claim请求`。
- 活动请求可以提交/取消：`AssistantViewModelUserDecisionTests` 全部通过。
- 页面离开取消已领取请求、重复完成和多 ViewModel Claim 竞态：既有测试继续通过。

## 验证命令

- `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AssistantViewModelUserDecisionTests|FullyQualifiedName~AssistantDecisionLifecycleTests" --no-restore`：9/9 通过。
- 默认并行执行 `dotnet test LoomX.slnx --no-restore` 时，46 个既有 Avalonia UI 测试因 `Call from invalid thread` 失败；失败跨多个未改模块，属于测试并行线程约束。
- 使用 xUnit 串行设置重跑完整套件：1063/1063 通过。
- `dotnet build LoomX.slnx -c Release --no-restore`：通过，0 error。
- `openspec validate fix-assistant-decision-subscription-lifecycle --strict`：通过。
- `scripts/publish-desktop.ps1 -Configuration Release -OutputDirectory outputs\\2026-09-20-1914-assistant-decision-subscription-lifecycle`：通过，目录仅包含应用入口 `LoomX.exe`。

## 已知警告

- NuGet 报告 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的既有 `NU1903` 安全警告。
- 既有 `SettingsViewModel` 空值警告、`AnthropicResponseMapper` CA2024 和测试 CS8602 警告仍存在，本次未改动。

## 安全与一致性

- 未新增或记录 API Key、Authorization、用户 prompt、请求正文或工具参数。
- 未改变数据库路径、Broker 并发协议、Skill 工具协议或 Browser Bridge 租约协议。
- 代码实现与 proposal、design、delta spec 一致，无未接受偏差。
