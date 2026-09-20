# Verification Report: fix-new-provider-crash

## 摘要

| 维度 | 状态 |
|---|---|
| 完整性 | 3/3 任务完成；本次为既有行为修复，未新增 delta spec |
| 正确性 | 崩溃根因已由失败测试复现，修复后相关测试与真实发布包交互通过 |
| 一致性 | 实现遵循 design.md，仅在编辑态请求摘要预览处降级，不改变真实请求解析语义 |

## 验证证据

- 根因复现：新增回归测试在修复前抛出 `UriFormatException`，调用链为 `BindProvider` → `RefreshRequestSummary` → `ResolveEndpoint`。
- 回归测试：`ProviderTestPanelViewModelTests` 18/18 通过；在 force-push 后的 `origin/master`（`8e756ff`）上与 `ProvidersViewContractTests` 组合验证 55/55 通过。
- 完整测试：修复分支早期源码树曾得到 1058/1058 通过；后续重复运行暴露下述既有 Avalonia 线程波动。源仓库 force-push 后，本次以仅移植两个修复提交的方式重放到新 `origin/master`，未把旧合并/Revert 历史带回。
- 构建：`dotnet build LoomX.slnx -c Release --no-restore` 通过，0 错误。
- 发布：基于 force-push 后新 `master` 的 `outputs/20260920-054553-fix-new-provider-crash-master` 已生成。
- 手动验证：通过 Codex Computer Use 启动并校验上述发布包进程路径，使用鼠标光标坐标点击“新增 Provider”；Provider 数量从 7 增至 8，新 Provider 被选中且进程保持存活。早期修复包进入“测试”Tab 后还验证了 `POST · direct · 0 Headers`。
- 安全检查：改动未新增日志、密钥、请求正文或用户 prompt 输出。

## 实现映射

- `LoomX/ViewModels/ProviderTestPanelViewModel.cs:276`：捕获当前 Provider 快照，避免绑定切换期间重复读取可变字段。
- `LoomX/ViewModels/ProviderTestPanelViewModel.cs:285`：仅在 Base URL 可解析为绝对 URI 时生成完整预览地址，否则降级为 `POST`。
- `LoomX.Tests/Desktop/ProviderTestPanelViewModelTests.cs:91`：覆盖空 Base URL 的新建 Provider 请求摘要。

## 问题分级

### CRITICAL

无。

### WARNING

- 测试工程存在既有的 Avalonia UI 线程亲和性不稳定：后续重复运行完整测试时，部分与本次改动无关的 UI 测试会因 `Call from invalid thread` 随测试顺序波动；将非 UI 测试与 UI 测试拆分到独立进程后，非 UI 测试 882/882 通过，UI/Avalonia 测试除既有 `AssistantDecisionLifecycleTests` 同类线程问题外均可独立通过。该问题不由本次两个实现文件引入。

### SUGGESTION

- 后续可单独修复 Avalonia 测试引导与线程调度，使完整测试套件稳定重复运行。

## 最终结论

当前崩溃修复的任务、实现、回归测试、Release 构建、发布包和真实 UI 场景均已验证通过，可以进入分支处理；保留上述既有测试基础设施警告。
