---
comet_change: improve-startup-splash-screen
role: technical-design
canonical_spec: openspec
---

# Design

## Context

当前启动路径把主窗口、数据快照和插件运行时混在一起：

- `App.OnFrameworkInitializationCompleted` 先创建 `MainWindow` 和 `MainWindowViewModel`。
- `MainWindowViewModel` 构造函数里创建 `AssistantViewModel`。
- `AssistantViewModel` 构造函数会调用 `RefreshModelSummary()`，进而触发 `EnsureHostedServicesAsync()`，导致插件运行时在主窗口构造阶段被隐式拉起。
- `AppDataStore.InitializeCoreAsync()` 目前分别调用 6 个配置读取方法，导致同一配置库被反复初始化和重载。
- 用户可见的“加载中”只是 `PlaceholderView` 的一行文案，不是真正的启动闪屏。

这解释了两个问题：启动等待时间长，以及等待期间没有可理解的阶段反馈。

## Goals / Non-Goals

**Goals:**

- 提供一个独立、真实的启动闪屏窗口。
- 把启动过程拆成可观察的真实阶段：加载数据库、编译插件、加载插件、启动网关、准备主界面。
- 把配置库初始化收敛为一次快照读取，减少重复开销。
- 避免主窗口构造过程隐式触发插件运行时。
- 保持现有本地化、主题和测试约定。

**Non-Goals:**

- 不重写桌面端整体启动架构。
- 不改变配置数据库结构。
- 不改变插件包布局或插件运行时契约。
- 不引入新的外部依赖。

## Decisions

1. **新增 `StartupOrchestrator`**
   - 由它统一编排启动阶段，而不是把阶段判断散落在 `App`、`MainWindowViewModel` 和 `AssistantViewModel` 里。
   - `App` 只负责创建闪屏、调用编排器，并在启动完成后切换到主窗口。
   - 这样可以让“启动阶段”和“主窗口职责”分离，也便于测试阶段顺序。

2. **新增 `StartupProgress` / `StartupPhase`**
   - 用一个明确的阶段模型驱动闪屏，而不是在 UI 层伪造进度。
   - 阶段至少包含：`Database`、`PluginCompile`、`PluginLoad`、`Gateway`、`Shell`、`Completed`、`Failed`。
   - 闪屏只展示编排器和插件运行时上报的真实阶段，不展示假进度。

3. **新增独立 `SplashWindow`**
   - 使用独立窗口承载闪屏，而不是把 `PlaceholderView` 当成闪屏。
   - 视觉上复用现有主题资源和品牌标识，展示当前阶段、已完成阶段和总体进度。
   - 启动完成或失败后关闭，不参与后续主窗口导航。

4. **配置库改为一次启动快照**
   - 在 `ConfigSnapshotService` 新增 `LoadStartupSnapshotAsync()`，一次完成配置库初始化并返回完整启动快照。
   - `AppDataStore.InitializeCoreAsync()` 只调用这个方法一次，不再连续调用 6 个独立读取方法。
   - 现有写路径和局部更新逻辑保持不变，避免影响后续配置变更。

5. **插件运行时显式编排**
   - 给 `PluginRuntime` 增加真实阶段上报能力，用于区分“编译插件”和“加载插件”。
   - `GatewayProcessService` 暴露插件运行时进度事件，`StartupOrchestrator` 订阅并转发给闪屏。
   - 主窗口构造阶段不再隐式调用 `EnsureHostedServicesAsync()`；插件运行时改由启动编排器显式初始化。

6. **`AssistantViewModel` 延迟初始化**
   - 移除构造函数里的 `RefreshModelSummary()` 隐式触发。
   - 改为在主窗口就绪后或进入助手页时显式初始化。
   - 这样避免主窗口构造阶段把插件运行时提前拉起，同时保留助手功能。

## Risks / Trade-offs

- **阶段粒度过细会让实现复杂** → 采用粗粒度真实阶段，不追求每个插件逐条进度。
- **单次快照读取可能影响后续配置变更语义** → 只替换初始加载路径，写路径和局部刷新保持原逻辑。
- **延迟助手初始化可能让首次进入助手页多一次等待** → 在主窗口就绪后立即显式初始化助手服务，避免用户可感知延迟。
- **闪屏窗口在启动失败时需要清晰反馈** → 启动失败时显示安全错误摘要，并允许用户关闭闪屏或进入主窗口错误态。

## Migration Plan

1. 先增加启动阶段模型与闪屏窗口，不改变现有启动顺序。
2. 再接入一次性启动快照，减少配置库重复初始化。
3. 然后显式编排插件运行时与网关启动，移除 `AssistantViewModel` 的隐式触发。
4. 最后补充测试并验证真实启动日志，确认配置库初始化次数和插件运行时启动次数下降。

回滚方式：本变更为独立提交，可直接 revert，不涉及数据迁移。
