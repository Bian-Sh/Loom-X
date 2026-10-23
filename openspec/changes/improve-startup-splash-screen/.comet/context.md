# Comet Design Handoff

- Change: improve-startup-splash-screen
- Phase: design
- Mode: compact
- Context hash: 13b0c5d7b99bb968cccbe6926f8f012b70347db835d2a391ed930aba19816f1b

Generated-by: comet-handoff.sh
Task hash policy: task-content-v1. Read tasks.md for live completion; excerpts are design-time context.

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/improve-startup-splash-screen/proposal.md

- Source: openspec/changes/improve-startup-splash-screen/proposal.md
- Lines: 1-34
- SHA256: 3c950f29db6581f44c05a8eefd96e8c9dc9c2e4d4a0571b55ac55a65cf0944a7

```md
# Proposal

## Why

当前桌面启动没有真正的闪屏，只是在主窗口里显示一行占位文案；同时启动链路存在重复读库和过早拉起插件运行时的问题，导致用户在看似“卡住”的界面上等待很久。需要把启动过程改成可感知、可解释、可验证的专用闪屏体验，并顺手消除最明显的重复初始化开销。

## What Changes

- 新增桌面启动闪屏能力：应用启动时显示独立的启动闪屏窗口，主窗口在其完成后才进入可用状态。
- 闪屏展示真实的启动阶段与整体进度，至少覆盖：初始化配置数据库、编译插件、加载插件、启动网关、准备主界面。
- 新增启动进度模型，负责驱动阶段状态与进度条，而不是在 UI 层写死假进度。
- 优化启动链路，减少同一配置库的重复初始化与重复读取，并避免在主窗口构造阶段过早触发插件运行时。
- 替换当前“此页面已经完成导航接入...”这类占位文案，让启动阶段和主窗口职责分离。
- 保持现有本地化、主题与可访问性约定。

## Capabilities

### New Capabilities
- `desktop-startup-splash`: 定义桌面启动闪屏、启动进度、阶段可见性和启动完成切换行为。

### Modified Capabilities
- 无。现有 `desktop-startup-lifecycle` 的网关意图恢复与开机自启动语义不变，本变更只调整其呈现与初始化编排，不改变已有需求。

## Impact

- `LoomX/App.axaml.cs`
- `LoomX/MainWindowViewModel.cs`
- `LoomX/Services/*`
- `LoomX/Views/*`
- `LoomX/Resources/Strings*.resx`
- `LoomX.Tests/*`
- `LoomX.PluginHost/*`

不涉及数据库结构变更，不改变插件包布局，不改变用户数据或配置接口。

```

## openspec/changes/improve-startup-splash-screen/design.md

- Source: openspec/changes/improve-startup-splash-screen/design.md
- Lines: 1-84
- SHA256: dbe73d58bb7bf56a289626e3a99bd59536be374307224eff08fdcfb27d7855e5

[TRUNCATED]

```md
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

```

Full source: openspec/changes/improve-startup-splash-screen/design.md

## openspec/changes/improve-startup-splash-screen/tasks.md

- Source: openspec/changes/improve-startup-splash-screen/tasks.md
- Lines: 1-25
- SHA256: 8b945e892efa45e7ba5d84b2d4e6285e9384f58cd2cd7fe8889dd78c3cfab178

```md
# Tasks

## 1. 启动进度模型与配置快照

- [ ] 1.1 新增 `StartupPhase` / `StartupProgress` 模型，覆盖数据库、插件编译、插件加载、网关、主界面、完成、失败等状态，并补齐阶段推进与失败状态的单元测试
- [ ] 1.2 在 `ConfigSnapshotService` 增加一次性启动快照读取 API，确保一次初始化后返回完整配置数据，并用测试验证不再重复打开/初始化同一配置库
- [ ] 1.3 将 `AppDataStore.InitializeCoreAsync()` 改为使用一次性启动快照，保留现有写路径与局部刷新逻辑，并用回归测试确认初始化阶段只触发一次数据库读取

## 2. 插件运行时显式编排

- [ ] 2.1 给 `PluginRuntime` 增加真实的编译/加载阶段上报能力，并通过单元测试确认阶段顺序与完成状态准确
- [ ] 2.2 调整 `GatewayProcessService` / `LoomXHost` 的初始化顺序，使插件运行时由启动编排器显式拉起，而不是由主窗口构造隐式触发，并补充对应集成测试
- [ ] 2.3 移除 `AssistantViewModel` 构造函数中的隐式 `EnsureHostedServicesAsync()` 调用，改为启动完成后显式初始化助手服务，并测试主窗口构造不再触发插件运行时

## 3. 启动闪屏 UI

- [ ] 3.1 新增独立 `SplashWindow` 与 `SplashViewModel`，展示品牌、版本、阶段列表、当前阶段和总体进度，并通过 UI 契约测试验证布局与绑定
- [ ] 3.2 在 `App` 启动流程接入 `StartupOrchestrator`，按“加载数据库 → 编译插件 → 加载插件 → 启动网关 → 准备主界面”顺序切换闪屏阶段，并在完成后关闭闪屏、显示主窗口
- [ ] 3.3 移除当前概览页占位加载文案与奇怪的“导航接入”文案，把加载职责完全收敛到启动闪屏，并更新相关资源与本地化测试

## 4. 集成验证

- [ ] 4.1 运行启动链路相关测试，确认启动顺序、阶段上报、单次读库和主窗口构造行为全部通过
- [ ] 4.2 用真实启动日志测量优化前后的启动耗时与配置库初始化次数，确认启动等待明显下降且没有引入新的重复初始化
- [ ] 4.3 执行整体回归测试与本地化校验，确认新增闪屏文案、主题适配和既有功能没有回归

```

## openspec/changes/improve-startup-splash-screen/specs/desktop-startup-splash/spec.md

- Source: openspec/changes/improve-startup-splash-screen/specs/desktop-startup-splash/spec.md
- Lines: 1-87
- SHA256: 2a3eb7f41d2360fc5d7ce4c1797fd1f72093812a2c57cbfa95ff71243d77374d

[TRUNCATED]

```md
# Spec Delta

## Purpose

为桌面端提供真实、可观察的启动闪屏体验，并约束启动阶段只能反映实际执行中的初始化工作。

## ADDED Requirements

### Requirement: 启动闪屏必须独立于主窗口

系统 SHALL 在桌面启动时显示独立的启动闪屏窗口。闪屏窗口 MUST NOT 作为主窗口内容页呈现，MUST 在主窗口尚未可用时承担启动反馈职责，并在启动完成后退出或隐藏。

#### Scenario: 正常启动

- **WHEN** 用户启动桌面应用
- **THEN** 系统显示独立启动闪屏
- **AND** 闪屏展示品牌标识、当前启动阶段和整体进度
- **AND** 主窗口在启动流程完成前不进入可用状态

#### Scenario: 启动完成

- **WHEN** 启动流程完成
- **THEN** 系统关闭或隐藏启动闪屏
- **AND** 主窗口进入可用状态并展示概览页

### Requirement: 启动进度必须反映真实阶段

系统 SHALL 在启动过程中维护真实启动阶段与总体进度，不得伪造与实际执行状态无关的阶段文案或进度值。系统 SHALL 至少能呈现配置数据库加载、插件编译、插件加载、网关启动和主界面准备五个阶段。

#### Scenario: 阶段推进

- **WHEN** 启动流程进入新的实际阶段
- **THEN** 闪屏同步展示该阶段
- **AND** 闪屏对应的阶段状态从“进行中”变为“已完成”
- **AND** 总体进度按阶段推进

#### Scenario: 阶段失败

- **WHEN** 某个启动阶段失败
- **THEN** 闪屏或主窗口展示安全、可理解的错误反馈
- **AND** 系统不得把失败阶段标记为完成

### Requirement: 启动初始化必须避免重复读库

系统 SHALL 在一次启动过程中以一次配置库读取结果构造启动所需的完整配置快照，MUST NOT 在同一启动流程内重复打开、重复初始化或重复加载同一配置库来组装同一份快照。

#### Scenario: 配置库只加载一次

- **WHEN** 桌面启动需要读取配置数据库
- **THEN** 系统只执行一次配置库初始化与快照读取
- **AND** 后续启动阶段复用该快照构造主窗口状态

#### Scenario: 启动阶段仍然可见

- **WHEN** 配置数据库初始化与读取完成
- **THEN** 闪屏把配置数据库阶段标记为完成
- **AND** 系统继续进入插件或网关相关阶段

### Requirement: 插件运行时不得在主窗口构造阶段提前拉起

系统 SHALL 将插件运行时的发现、编译与加载编排到启动闪屏阶段或启动完成后的明确初始化阶段，MUST NOT 在主窗口构造过程中隐式触发插件运行时初始化。

#### Scenario: 主窗口构造

- **WHEN** 主窗口及其视图模型正在构造
- **THEN** 系统不隐式启动插件运行时
- **AND** 闪屏持续展示当前真实启动阶段

#### Scenario: 插件阶段完成

- **WHEN** 插件运行时完成发现、编译与加载
- **THEN** 闪屏把插件相关阶段标记为完成
- **AND** 系统继续执行剩余启动阶段

### Requirement: 启动闪屏必须遵守本地化与主题约定

系统 SHALL 通过统一资源解析闪屏用户可见文案，并复用现有主题与视觉体系。闪屏 MUST NOT 在 AXAML 或 ViewModel 中硬编码中文文案。

#### Scenario: 切换语言


```

Full source: openspec/changes/improve-startup-splash-screen/specs/desktop-startup-splash/spec.md
