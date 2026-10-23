# Proposal

## Why

实测（2026-10-24 发布版 + 启动日志 `%LOCALAPPDATA%\LoomX\logs\loomx-20261024.log`）发现：启动快照虽然在闪屏的 Database 阶段只读取了一次，但随后的 Shell 阶段（主窗口初始化，闪屏尚未关闭）又对配置库做了两次完整读取——Providers 页面刷新触发了一次「配置快照同步读取」（00:23:07.591），助手模型摘要又单独打开配置库读 Provider 列表（00:23:08.211）。这违反了 `desktop-startup-splash` 中「后续启动阶段复用该快照构造主窗口状态」的场景约定，并给每次启动增加约 1 秒无谓等待。

## What Changes

- Shell 阶段的 Providers 页面初始化改为复用数据中心（AppDataStore）已加载的启动快照，不再触发 `ConfigSnapshotService` 的全量同步读取。
- 助手 `InitializeAfterStartup` → `RefreshModelSummary` 的首次模型摘要不再单独打开配置库读 Provider 列表，复用内存中已加载的 Provider 数据。
- 明确拆分「初始化读库（整个启动流程内仅一次）」与「显式刷新（用户操作/配置事件驱动，按需读库）」两条路径的 API 语义；显式刷新行为保持现状不变。
- 扩大验证：新增覆盖「启动编排器 → Shell 阶段 → 主窗口初始化」全链路的回归测试，断言配置库只初始化/读取一次；并校验日志中不出现无事件驱动的第二次库初始化。

## Capabilities

### New Capabilities

- 无。

### Modified Capabilities

- `desktop-startup-splash`：把「启动初始化必须避免重复读库」的要求范围从「启动快照组装」扩展到「整个启动流程（含主界面/Shell 阶段与主窗口初始化）」，并新增「无事件驱动不得读库」的验证场景。依赖 `improve-startup-splash-screen` 已合入的能力定义（该 change 代码已进 master，尚未归档）。

## Impact

- `LoomX/ViewModels/MainWindowViewModel.cs`（`InitializeAfterStartupAsync` / `ApplyLoadedConfiguration` / `ProvidersViewModel.RefreshCoreAsync`）
- `LoomX/ViewModels/AssistantViewModel.cs`（`InitializeAfterStartup` / `RefreshModelSummary`）
- `LoomX/Services/AppDataStore.cs`（初始化与刷新语义拆分）
- `LoomX/Services/ConfigSnapshotService.cs`（如需为计数测试暴露可观测点）
- `LoomX.Tests/*`（新增启动读库次数回归测试）

不涉及数据库结构变更，不改变任何写路径与用户数据，不改变插件包布局。
