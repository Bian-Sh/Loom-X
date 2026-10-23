# Tasks

## 1. 语义拆分与启动链路改造

- [ ] 1.1 为 `AppDataStore` 引入「仅在未初始化时读库」的入口（如 `EnsureInitializedAsync`），已初始化时直接返回内存态；`InitializeAsync` 的显式刷新语义保持不变
- [ ] 1.2 `ProvidersViewModel.RefreshCoreAsync` 启动首刷改用上述入口，消除 `ConfigSnapshotService.Load()` 的全量同步读取（日志「配置快照同步读取开始」在启动流程内不再出现）
- [ ] 1.3 `AssistantViewModel.RefreshModelSummary` 首次模型摘要复用 `dataStore.Providers`（保持 `SortOrder` → `ModelId` 分组排序与启动快照一致），仅数据中心未初始化时回落 `ListProvidersAsync`

## 2. 扩大验证（拒绝无谓数据库加载）

- [ ] 2.1 为「打开/初始化配置库次数」建立可观测点（可计数 DbContextFactory 或 Serilog 测试 sink 断言），先以测试复现当前 Shell 阶段两次重复读库（红）
- [ ] 2.2 新增集成回归测试：完整启动链路（`StartupOrchestrator.RunAsync` → `InitializeAfterStartupAsync`）配置库初始化/读取恰好一次（绿）
- [ ] 2.3 新增反向用例：启动完成后显式刷新 / 配置变更事件仍会按需读库，证明显式刷新语义未被破坏
- [ ] 2.4 回归验证 Provider 编辑保存后页面数据一致、助手模型选择器分组顺序不变

## 3. 收尾验证

- [ ] 3.1 全量 `dotnet test`（LoomX.Tests）通过；`PluginPayloadLayoutTests` 等布局契约不受影响
- [ ] 3.2 发布版实测：启动日志从进程开始到主窗口可交互期间「配置数据库初始化完成」类条目只出现一次，记录优化前后 Shell 阶段耗时对比
