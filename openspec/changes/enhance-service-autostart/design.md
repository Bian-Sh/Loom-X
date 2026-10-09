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
- 不新增第二套配置来源或额外配置文件。
- 不改变网关的端口、协议或健康检查规则。

## Decisions

### 1. 在 AppSettings 中保存两个布尔字段

新增 `StartWithWindows` 和 `GatewayRunning`，默认值均为 `false`。二者进入现有设置响应与运行时快照；`GatewayRunning` 不进入普通设置输入，设置页保存时不会写入该字段。

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
- [设置页普通保存覆盖 GatewayRunning] → `AppSettingsInput` 不包含 `GatewayRunning`，普通设置保存不会写入该字段，网关意图仅走单字段更新接口。

## Configuration Change Plan

- 当前配置结构初始化时确保 `AppSettings` 包含 `StartWithWindows` 和 `GatewayRunning`，默认均为 `false`。
- 配置访问继续统一通过 `AppDataPaths` 指向 `%LOCALAPPDATA%\LoomX\LoomX.db`。
- 回滚代码时可以忽略数据库中多出的布尔列；禁用自启动可通过设置开关或删除当前用户 `Run` 值完成。
