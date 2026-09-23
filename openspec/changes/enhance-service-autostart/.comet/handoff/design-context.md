# Comet Design Handoff

- Change: enhance-service-autostart
- Phase: design
- Mode: compact
- Context hash: e5de34f88f18ccdf36302a91a3750f6370988a832a1bc13faaab289a2ddf5d61

Generated-by: comet-handoff.sh
Task hash policy: task-content-v1. Read tasks.md for live completion; excerpts are design-time context.

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/enhance-service-autostart/proposal.md

- Source: openspec/changes/enhance-service-autostart/proposal.md
- Lines: 1-31
- SHA256: 1e6ec6a5edec63162c36ac46aa59723e7e4649565949859a3712d9f0fb718022

```md
## Why

Loom-X 当前需要用户每次登录后手动启动应用，并且每次打开应用都要重新决定是否启动网关。为减少重复操作，应提供可选的 Windows 开机自启动，并让概览页的网关启动/关闭操作成为可持久恢复的运行意图。

## What Changes

- 在设置页新增“开机时启动 Loom-X”开关，启用或关闭当前用户级 Windows 自启动注册。
- 持久化 `GatewayRunning` 运行意图；只有概览页“启动网关”和“关闭网关”对应的函数可以翻转该值。
- APP 初始化完成后读取 `GatewayRunning`；值为 `true` 时自动启动网关，值为 `false` 时保持停止。
- APP 退出时仍停止并释放网关资源，但不改变持久化的 `GatewayRunning`。
- 网关自动恢复失败时保持运行意图为 `true`，由现有状态、日志和概览页反馈失败，下次启动继续尝试。
- 补齐新增设置文案的 zh-CN、zh-TW、en-US 和 ja-JP 本地化资源。

## Capabilities

### New Capabilities

- `desktop-startup-lifecycle`: 规定 Windows 开机自启动设置和桌面应用恢复网关运行意图的行为。

### Modified Capabilities

- 无。

## Impact

- 配置：`AppSettings` 增加开机自启动和网关运行意图字段，并继续使用 `%LOCALAPPDATA%\LoomX\LoomX.db`。
- 桌面启动：`App.axaml.cs` 在初始化后恢复网关运行意图，退出时只释放服务。
- 网关交互：概览页启动/关闭命令负责更新运行意图。
- 设置界面：`SettingsViewModel`、`SettingsView.axaml` 和四套本地化资源增加开机自启动配置。
- Windows 集成：新增当前用户级自启动注册服务，不需要管理员权限。
- 测试与发布：增加配置、启动注册、网关状态恢复和 UI 契约测试，并重新生成 win-x64 发布产物。

```

## openspec/changes/enhance-service-autostart/design.md

- Source: openspec/changes/enhance-service-autostart/design.md
- Lines: 1-80
- SHA256: cb0a0128998e6a2935e43231c4f3693a9719b280ed1a7d08503fbf14213d10e4

```md
---
comet_change: enhance-service-autostart
role: technical-design
canonical_spec: openspec
---

## Context

参见 `proposal.md` 的动机和 `specs/desktop-startup-lifecycle/spec.md` 的行为契约。当前设置通过 `AppSettingsEntity`、`ConfigurationManagementService`、`ConfigSnapshotService` 和 `AppDataStore` 统一进入 `%LOCALAPPDATA%\LoomX\LoomX.db`；概览页通过 `OverviewViewModel` 调用 `GatewayProcessService`；APP 退出路径会无条件停止网关以释放端口和托管服务。

Windows 是当前唯一支持的桌面发布目标。现有启动流程已经支持单实例和 Shell 子进程引导，因此开机自启动只需启动当前可执行文件，不另建守护进程。

## Goals / Non-Goals

**Goals:**

- 使用当前用户权限控制 Windows 开机自启动，不要求管理员权限。
- 将用户在概览页作出的网关启动/关闭选择持久化为运行意图。
- APP 启动时恢复运行意图，退出清理时不覆盖该意图。
- 复用现有网关启动、健康检查、状态展示和日志链路。

**Non-Goals:**

- 不增加独立后台服务、托盘进程或 Windows Service。
- 不根据健康检查、启动失败或退出清理自动改写网关运行意图。
- 不增加“APP 启动后自动启动网关”设置开关。
- 不引入旧产品数据库扫描、旧路径迁移或额外配置文件。
- 不改变网关的端口、协议或健康检查规则。

## Decisions

### 1. 在 AppSettings 中保存两个布尔字段

新增 `StartWithWindows` 和 `GatewayRunning`，默认值均为 `false`。二者进入现有设置响应与运行时快照；设置页保存完整设置时保留 `GatewayRunning` 当前值，避免普通设置保存覆盖网关运行意图。

`GatewayRunning` 使用独立的配置更新方法，只更新这一字段并刷新 `AppDataStore` 快照。这样概览页不需要构造完整 `AppSettingsInput`，也不会与设置页其他字段形成不必要耦合。

备选方案是把网关意图写入独立 JSON 文件，但这会制造第二套配置来源，违背统一配置数据库约束，因此不采用。

### 2. 只有概览页网关操作写入 GatewayRunning

`OverviewViewModel` 的启动、关闭和切换命令在执行对应网关操作前，先通过 `AppDataStore` 写入用户意图：启动写 `true`，关闭写 `false`。自动恢复、运行时状态事件和 APP 退出直接调用 `GatewayProcessService`，不调用这些意图更新方法。

选择“先持久化意图、再操作网关”，因为该字段描述用户选择而不是本次启动是否成功；即使网关启动失败，下次 APP 启动也应继续尝试恢复。若意图持久化失败，则不继续执行网关操作，避免当前行为与下次恢复行为不一致，并通过现有异步命令日志记录失败。

### 3. 配置初始化后执行一次启动恢复

`MainWindowViewModel.InitializeDataStoreAsync` 在 `AppDataStore.InitializeAsync` 成功后执行两项相互独立的恢复：

1. 根据 `StartWithWindows` 校准 Windows 自启动注册；
2. 当 `GatewayRunning` 为 `true` 时，使用当前配置的首个监听 URL 调用 `GatewayProcessService.StartAsync`。

恢复逻辑直接调用网关服务，不经过概览页命令，因此不会再次写入 `GatewayRunning`。网关服务现有实现会把失败转换为 `Failed` 状态，APP 初始化继续完成。

### 4. 使用当前用户 Run 注册表项实现开机自启动

新增 Windows 自启动服务，操作 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下名为 `LoomX` 的值。启用时写入带引号的当前可执行文件绝对路径，关闭时删除该值。每次 APP 配置初始化和设置保存后都按期望值校准一次，使应用更新后启动路径能够在下次正常运行时刷新。

该方案不需要管理员权限，也不增加外部依赖。相比 Startup 文件夹快捷方式，它不需要 COM；相比 Windows Service，它符合桌面应用生命周期且复杂度更低。

注册失败不会终止 APP；调用方显示错误 Toast 并记录结构化日志。数据库中的 `StartWithWindows` 继续表示用户期望值，后续启动或再次保存时可以重试校准。

### 5. APP 退出只负责资源释放

保留 `App.axaml.cs` 退出处理中的 `GatewayProcessService.StopAsync`。退出路径不调用 `AppDataStore.SetGatewayRunningAsync(false)`，因此持久化意图不会被资源清理覆盖。

## Risks / Trade-offs

- [注册表被安全软件或系统策略阻止] → 保持 APP 可用，显示错误 Toast，记录不包含敏感信息的结构化错误日志。
- [可执行文件位置在更新后发生变化] → 每次正常启动和设置保存时重新写入当前路径。
- [用户点击启动后网关实际启动失败] → `GatewayRunning` 保持 `true`，现有失败状态明确展示，下一次 APP 启动继续尝试。
- [意图写入与网关操作并发] → 复用 `AppDataStore` 和配置服务现有串行写入边界，概览页切换命令继续禁止启动/停止过渡期重复点击。
- [设置页普通保存覆盖 GatewayRunning] → 构造 `AppSettingsInput` 时使用当前设置快照中的 `GatewayRunning`，网关意图另走单字段更新接口。

## Configuration Change Plan

- 当前配置结构初始化时确保 `AppSettings` 包含 `StartWithWindows` 和 `GatewayRunning`，默认均为 `false`。
- 不读取或写入 `%LOCALAPPDATA%\OllamaHub`，不增加任何旧产品数据库处理。
- 回滚代码时可以忽略数据库中多出的布尔列；禁用自启动可通过设置开关或删除当前用户 `Run` 值完成。


```

## openspec/changes/enhance-service-autostart/tasks.md

- Source: openspec/changes/enhance-service-autostart/tasks.md
- Lines: 1-20
- SHA256: 28e38a37ecb7358a45792dc0c4cedd162a405caac859f8410ac879f8c934c801

```md
## 1. 配置契约与持久化

- [ ] 1.1 先增加失败测试，覆盖 `StartWithWindows`、`GatewayRunning` 默认值、设置读写和当前配置结构初始化，再实现 AppSettings 实体、输入/响应、运行时快照及数据库列并验证相关配置测试通过
- [ ] 1.2 先增加失败测试，覆盖单字段更新网关运行意图且不改变其他设置，再实现 ConfigurationManagementService、ConfigSnapshotService 和 AppDataStore 的 `SetGatewayRunningAsync` 链路并验证测试通过

## 2. Windows 开机自启动

- [ ] 2.1 先增加失败测试，覆盖当前用户 Run 值的命令格式、启用和移除行为，再实现 Windows 自启动注册服务并验证测试通过
- [ ] 2.2 在设置 ViewModel、设置页和四套本地化资源中加入“开机时启动 Loom-X”，验证设置加载/保存、资源覆盖率和 AXAML 契约测试通过

## 3. 网关运行意图恢复

- [ ] 3.1 先增加失败测试，验证概览页启动写入 `true`、关闭写入 `false`，且自动状态变化不写入，再修改���览页命令并验证测试通过
- [ ] 3.2 先增加失败测试，验证 APP 初始化仅在 `GatewayRunning = true` 时恢复网关、启动失败不清零意图、退出清理不改意图，再接入应用启动流程并验证测试通过

## 4. 集成验证与交付

- [ ] 4.1 运行 OpenSpec 严格校验、相关测试、完整测试和 Release 构建，确认无失败或新增警告
- [ ] 4.2 使用隐藏启动方式运行发布包并通过 CUA 验收设置开关、概览页网关启停与重启恢复行为，记录实际结果
- [ ] 4.3 生成 win-x64 发布包，以可读时间命名保存到 `outputs/`，验证可执行文件路径和产物内容完整

```

## openspec/changes/enhance-service-autostart/.openspec.yaml

- Source: openspec/changes/enhance-service-autostart/.openspec.yaml
- Lines: 1-2
- SHA256: d04b6ad7c1b1e5eb9e8ec8cfdae94bc49d29216b59b73cea7a3cc1de9d508463

```md
schema: spec-driven
created: 2026-09-23

```

## openspec/changes/enhance-service-autostart/specs/desktop-startup-lifecycle/spec.md

- Source: openspec/changes/enhance-service-autostart/specs/desktop-startup-lifecycle/spec.md
- Lines: 1-87
- SHA256: 7d8d8543d0db9baa990bf9fbe4076c07003d350509813da77e9e38831b6d386b

[TRUNCATED]

```md
## Purpose

定义 Loom-X 在 Windows 登录后的可选自动启动行为，以及桌面应用跨运行周期恢复用户明确选择的网关运行意图。

## ADDED Requirements

### Requirement: 用户可以配置开机自启动

系统 SHALL 在设置页提供“开机时启动 Loom-X”开关，并将该选择持久化到当前 Loom-X 配置数据库。启用时系统 SHALL 为当前 Windows 用户注册 Loom-X 自启动项，关闭时 SHALL 移除该自启动项，且整个过程不得要求管理员权限。

#### Scenario: 启用开机自启动

- **WHEN** 用户开启“开机时启动 Loom-X”
- **THEN** 系统保存该设置
- **AND** 当前 Windows 用户下次登录时自动启动 Loom-X

#### Scenario: 关闭开机自启动

- **WHEN** 用户关闭“开机时启动 Loom-X”
- **THEN** 系统保存该设置
- **AND** 移除当前 Windows 用户的 Loom-X 自启动项

#### Scenario: 自启动注册失败

- **WHEN** 系统无法注册或移除当前用户自启动项
- **THEN** APP 继续运行
- **AND** 系统通过用户可见反馈和结构化日志报告失败
- **AND** 不得将失败报告为操作成功

### Requirement: 概览页操作持久化网关运行意图

系统 SHALL 持久化布尔值 `GatewayRunning`。只有概览页“启动网关”和“关闭网关”对应的用户操作函数 SHALL 翻转该值；网关健康检查、运行时状态变化、自动恢复和 APP 退出清理不得修改该值。

#### Scenario: 用户启动网关

- **WHEN** 用户在概览页执行“启动网关”
- **THEN** 系统将 `GatewayRunning` 设置为 `true`
- **AND** 调用网关启动流程

#### Scenario: 用户关闭网关

- **WHEN** 用户在概览页执行“关闭网关”
- **THEN** 系统将 `GatewayRunning` 设置为 `false`
- **AND** 调用网关停止流程

#### Scenario: 网关运行时状态自行变化

- **WHEN** 网关因健康检查失败、启动失败或其他非概览页用户操作而进入非运行状态
- **THEN** 系统不修改 `GatewayRunning`

### Requirement: APP 启动时恢复网关运行意图

系统 SHALL 在配置和桌面窗口初始化完成后读取 `GatewayRunning`。值为 `true` 时 SHALL 自动调用网关启动流程；值为 `false` 时 SHALL 保持网关停止。自动恢复不得再次写入 `GatewayRunning`。

#### Scenario: 恢复运行中的网关意图

- **WHEN** APP 启动且 `GatewayRunning` 为 `true`
- **THEN** 系统自动启动网关
- **AND** 不修改 `GatewayRunning`

#### Scenario: 恢复停止的网关意图

- **WHEN** APP 启动且 `GatewayRunning` 为 `false`
- **THEN** 系统不自动启动网关

#### Scenario: 自动恢复失败

- **WHEN** APP 根据 `GatewayRunning = true` 自动启动网关但启动失败
- **THEN** APP 继续运行并展示现有网关失败状态
- **AND** `GatewayRunning` 保持为 `true`
- **AND** 下次 APP 启动时仍会再次尝试恢复

### Requirement: APP 退出不覆盖网关运行意图

系统 SHALL 在 APP 退出时停止并释放当前网关资源，但该退出清理流程不得修改 `GatewayRunning`。

#### Scenario: 网关运行意图为开启时退出

- **WHEN** `GatewayRunning` 为 `true` 且用户退出 APP
- **THEN** 系统停止并释放本次运行的网关资源

```

Full source: openspec/changes/enhance-service-autostart/specs/desktop-startup-lifecycle/spec.md
