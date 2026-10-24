# Tasks

## 1. 语义拆分与启动链路改造

- [x] 1.1 为 `AppDataStore` 引入「仅在未初始化时读库」的入口（如 `EnsureInitializedAsync`），已初始化时直接返回内存态；`InitializeAsync` 的显式刷新语义保持不变
- [x] 1.2 `ProvidersViewModel.RefreshCoreAsync` 启动首刷改用上述入口，消除 `ConfigSnapshotService.Load()` 的全量同步读取（日志「配置快照同步读取开始」在启动流程内不再出现）
- [x] 1.3 `AssistantViewModel.RefreshModelSummary` 首次模型摘要复用 `dataStore.Providers`（保持 `SortOrder` → `ModelId` 分组排序与启动快照一致），仅数据中心未初始化时回落 `ListProvidersAsync`

## 2. 扩大验证（拒绝无谓数据库加载）

- [x] 2.1 为「打开/初始化配置库次数」建立可观测点（可计数 DbContextFactory 或 Serilog 测试 sink 断言），先以测试复现当前 Shell 阶段两次重复读库（红）
- [x] 2.2 新增集成回归测试：完整启动链路（`StartupOrchestrator.RunAsync` → `InitializeAfterStartupAsync`）配置库初始化/读取恰好一次（绿）
- [x] 2.3 新增反向用例：启动完成后显式刷新 / 配置变更事件仍会按需读库，证明显式刷新语义未被破坏
- [x] 2.4 回归验证 Provider 编辑保存后页面数据一致、助手模型选择器分组顺序不变

## 3. 收尾验证

- [x] 3.1 全量 `dotnet test`（LoomX.Tests）通过；`PluginPayloadLayoutTests` 等布局契约不受影响
- [x] 3.2 发布版实测：启动日志从进程开始到主窗口可交互期间「配置数据库初始化完成」类条目只出现一次，记录优化前后 Shell 阶段耗时对比

## 实施记录（2026-10-24）

- 2.2/2.4：`LoomX.Tests/Startup/StartupDatabaseReadOnceTests` 以 `ConfigSnapshotService.DatabaseReadCount` 为观测点断言 Shell 阶段（按 MainWindowViewModel 构造顺序构造全部页面 VM + 助手分组折叠）读库恰好一次、页面数据与启动快照一致；`AssistantModelClientFactoryTests.GroupAvailableModels_FromStartupSnapshot_MatchesDatabaseListing` 固定内存快照与数据库读取的分组顺序一致；`SplashStartupContractTests` 固定助手首刷的数据源优先级。MainWindowViewModel 外壳因无头测试的 Dispatcher 阻塞与 Geometry 平台依赖不可构造（见 design.md 测试基建备注）。
- 3.1：全量 1973/1973 通过（Debug，net10.0）。
- 3.2：发布版实测（outputs/20261024-031342，进程 34864）：启动窗口内「启动快照读取开始」1 次，「配置快照同步读取开始」「配置数据库初始化完成」「Provider 列表数据库初始化完成」均为 0 次（改造前实测分别为 1/1/1）。阶段耗时：Database 阶段启动快照 28.951→30.094（约 1.14s，与改造前一致）；Shell 阶段主窗口初始化 35.029→概览快照应用 35.197→Provider 首刷 35.204→设置加载 35.528，全程无配置库重复读取（改造前 Shell 阶段重复读库链路约 620ms：00:23:07.591→08.211）。启动窗口内另有 1 次「配置库读取前初始化完成」（03:14:35.545）来自更新协调器按 `AutoCheckUpdates` 设置的定向代理设置读取（DPAPI 保护密码按设计不落内存快照），不属于启动快照组装，维持现状。

