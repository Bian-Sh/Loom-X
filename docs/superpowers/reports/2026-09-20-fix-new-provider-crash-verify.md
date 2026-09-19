# Verification Report: fix-new-provider-crash

## 摘要

| 维度 | 状态 |
|---|---|
| 完整性 | 3/3 任务完成；本次为既有行为修复，未新增 delta spec |
| 正确性 | 崩溃根因已由失败测试复现，修复后相关测试与真实发布包交互通过 |
| 一致性 | 实现遵循 design.md，仅在编辑态请求摘要预览处降级，不改变真实请求解析语义 |

## 验证证据

- 根因复现：新增回归测试在修复前抛出 `UriFormatException`，调用链为 `BindProvider` → `RefreshRequestSummary` → `ResolveEndpoint`。
- 回归测试：`ProviderTestPanelViewModelTests` 18/18 通过；相关本地化回归合并验证 19/19 通过。
- 完整测试：曾在与最终 HEAD 内容完全相同的源码树上得到 1058/1058 通过；`git diff ca297da..HEAD` 为空，后续合并仅整合远端提交图。
- 构建：`dotnet build LoomX.slnx -c Release --no-restore` 通过，0 错误。
- 发布：`outputs/20260920-041817-fix-new-provider-crash` 已生成，发布目录 `LoomX.dll` 与 Release 产物哈希一致。
- 手动验证：通过 CUA 启动并校验上述发布包进程路径，点击“新增 Provider”后进程保持存活；进入“测试”Tab 后显示 `POST · direct · 0 Headers`。
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
