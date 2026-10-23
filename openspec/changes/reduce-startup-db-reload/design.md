# Design

## Context

启动闪屏重构（`improve-startup-splash-screen`，已合入 master @ ab8ca28）引入了 `StartupOrchestrator` 与 `ConfigSnapshotService.LoadStartupSnapshotAsync`：Database 阶段一次性读取完整启动快照（实测 8 providers / 27 models / 3 endpoints / 3 combos，约 1.75s），`AppDataStore.InitializeAsync(isInitialLoad: true)` 消费该快照。但实测日志显示 Shell 阶段仍有两次重复读库：

```
00:22:51.103  启动快照读取完成 8 27 3 3          ← 启动快照（唯一一次快照）
00:22:51.105  桌面启动阶段完成 Database
00:23:07.589  Provider 页面刷新开始，进程 9588     ← Shell 阶段开始（主窗口初始化）
00:23:07.591  配置快照同步读取开始                 ← 重复读库 #1（完整快照重新读一遍）
00:23:07.595  配置数据库初始化完成
00:23:08.211  Provider 列表数据库初始化完成        ← 重复读库 #2（助手模型摘要）
```

已确认的调用链：

1. **重复读库 #1**：`App.CompleteStartupAsync` → `MainWindowViewModel.InitializeAfterStartupAsync()` → `ApplyLoadedConfiguration()` → `ShowOverview()`；`ProvidersViewModel` 订阅 `dataStore.ConfigurationReady`/`ConfigurationChanged`（`OnConfigurationChanged`，MainWindowViewModel.cs:667）触发 `RefreshAsync()` → `RefreshCoreAsync()`（MainWindowViewModel.cs:1162）第一步 `await dataStore.InitializeAsync()`——该方法在已初始化时仍会重新走完整读库路径（非 initial 载入调用 `ConfigSnapshotService.LoadAsync` → 同步 `Load()` 打开配置库）。
2. **重复读库 #2**：`AssistantViewModel.InitializeAfterStartup()` → `RefreshModelSummary()`（AssistantViewModel.cs:1092）→ `EnsureServiceAsync()` → `service.ListAvailableModelsAsync()` → `ConfigSnapshotService.ListProvidersAsync`（对应日志「Provider 列表数据库初始化完成」，ConfigSnapshotService.cs:86），没有复用 `dataStore.Providers`。

约束：显式刷新语义必须保留——用户编辑 Provider、配置事件到达时的重新读库是正确行为，本 change 只针对启动流程内的无驱动读取。

### 根因修正（2026-10-24 实施期）

实施期逐行核实调用链后，修正上文对两次重复读库的归因：

1. **重复读库的真实共同来源是 `OverviewViewModel` 构造函数**（MainWindowViewModel.cs 内，构造顺序先于 ProvidersViewModel）：其构造时的 `_ = RefreshAsync()` 调用 `dataStore.RefreshAsync()`，以 `isInitialLoad: false` 走 `ConfigSnapshotService.Load()` 同步快照 + `ListProvidersAsync` + `GetSettingsAsync` 等共 6 个读入口，同时产出实测日志中的「配置快照同步读取开始」「配置数据库初始化完成」「Provider 列表数据库初始化完成」三条记录。上文把 #1 归因到 `ProvidersViewModel.RefreshCoreAsync` 的 `dataStore.InitializeAsync()` 与代码不符：`AppDataStore.InitializeAsync` 经 `initializationTask` memoize，编排器 Database 阶段完成后再次调用直接返回已完成任务，**不会重读**；因此该处只是语义不清，并非实际读库点（本次仍按 Decision 1 将其改为 `EnsureInitializedAsync` 以固定语义）。
2. **实测日志顺序错位的原因**是 `ConfigSnapshotService.LoadAsync` 内部用 `Task.Run` 把同步 `Load()` 丢进线程池：UI 线程继续构造 `ProvidersViewModel` 并打印「Provider 页面刷新开始」，线程池稍后才打印「配置快照同步读取开始」，造成「Providers 页面触发同步读取」的假象。归因时应以日志文本的产生位置（`ConfigSnapshotService` 各方法）而非相邻时间戳为准。
3. **预置测试缺陷备注**：并行会话预置的 `LoomX.Tests/Views/StartupShellDatabaseAccessTests.cs` 第 101 行断言 `dataStore.CreateProviderAsync`（产生 LocalSave 事件）后 Providers 页面自动出现新 Provider；而 `ProvidersViewModel.OnConfigurationChanged` 按既有约定过滤 LocalSave 事件（本机编辑保存不重建列表），语义保持的实现无法满足该断言。建议把建 Provider 移到显式刷新之前——显式刷新的 Snapshot 事件会让页面合并出一致数据，可保留「显式刷新按需读库且页面数据一致」的用例意图。


## Goals / Non-Goals

**Goals:**
- 整个启动流程（编排器 Database 阶段 → Shell 阶段 → 主窗口初始化完成）内配置库只初始化、读取一次；后续阶段全部复用内存快照。
- 保持显式刷新路径（用户操作、`ConfigurationChanged` 事件）行为完全不变。
- 用测试把「只读一次」固定下来，防止未来回归（扩大验证）。

**Non-Goals:**
- 不优化插件编译缓存（独立 change：`stabilize-plugin-compile-cache`）。
- 不改变闪屏 UI 与阶段模型。
- 不引入新的缓存层或跨进程共享内存。

## Decisions

1. **拆分 AppDataStore 的「初始化」与「刷新」语义**：新增/明确一个「仅在未初始化时读库」的入口（例如 `EnsureInitializedAsync`；已初始化直接返回），`ProvidersViewModel.RefreshCoreAsync` 在启动首刷场景使用该入口；`RefreshAsync`（显式刷新）继续调用会重读的 `InitializeAsync`。备选方案「给 RefreshAsync 加 skipRead 参数」被否：把启动特例埋进刷新签名，容易在未来把「跳过读库」误用到需要新数据的场景。
2. **助手首刷复用数据中心**：`RefreshModelSummary` 的模型分组数据源改为优先使用 `dataStore.Providers`（启动快照已在内存）；只有数据中心未初始化时才回落到 `ListProvidersAsync`。备选方案「把 ConfigSnapshotService 的内存态做成二级缓存」被否：多一份缓存多一份失效逻辑，数据中心已经是权威内存态。
3. **可测性**：为「打开/初始化配置库次数」提供可观测点（优先方案：测试内用可计数 DbContextFactory 或拦截 `EnsureDatabaseReadyForReadAsync`；备选：解析 Serilog 测试 sink 中的「数据库初始化完成」日志条数）。测试覆盖三层：AppDataStore 单元语义、StartupOrchestrator + `InitializeAfterStartupAsync` 集成链路、以及「ConfigurationChanged 到达时仍会读库」的反向用例（证明显式刷新没被误伤）。

## Risks / Trade-offs

- `ConfigurationReady` 事件在编排器初始化时已触发过一次（当时主窗口 VM 尚未订阅）；`InitializeAfterStartupAsync` 显式调用 `ApplyLoadedConfiguration` 属于启动路径，改造时需确认不会引入第二次事件回声（`OnConfigurationChanged` 已有 LocalSave 过滤，注意 Remote 来源事件在启动窗口期的表现）。
- `RefreshCoreAsync` 改用 `EnsureInitializedAsync` 后，「打开 Provider 页面前数据已过期」的场景由既有 `ConfigurationChanged` 刷新兜底；需在 tasks 中明确验证编辑保存 → 页面数据一致的回归。
- 助手模型摘要依赖 `service.GetModelSelection()` 与分组结构，复用 `dataStore.Providers` 时必须保持分组排序（`Provider.SortOrder` → `Model.SortOrder` → `ModelId`）与启动快照查询一致，避免选择器顺序跳变。

## Open Questions

- 无阻塞性问题；若实现中发现 `ConfigurationReady` 在启动窗口期存在第二次触发，按 Decision 1 的语义拆分处理并在测试中固化。
