# 服务自启动加强验证报告

## 总结

| 维度 | 结果 |
|---|---|
| 完整性 | PASS：9/9 个任务完成，4/4 个需求有实现证据 |
| 正确性 | PASS：11/11 个规格场景有自动化测试或 CUA 实机证据 |
| 一致性 | PASS：实现遵循统一配置库、HKCU 自启动、概览页独占意图写入和退出只清理资源的设计 |

**最终评估：没有 CRITICAL、WARNING 或 SUGGESTION 级实现问题，可以进入归档确认。** 完整测试仍有 2 个相对本 change 基线未修改的既有契约失败，已单独记录，不属于本次实现回归。

## 完整性

- `tasks.md`：9 项全部为 `[x]`。
- OpenSpec 严格校验：`comet classic openspec -- validate enhance-service-autostart --strict` 通过。
- 发布包、CUA 验收、配置恢复与验证报告均已生成。

## 正确性映射

### 1. 用户可以配置开机自启动

- 配置字段与数据库列：`LoomX/Configuration/ConfigurationDbContext.cs:38-39,414,475-476,530-531`。
- 当前用户 Run 注册实现：`LoomX/Services/WindowsStartupService.cs:8-82`。
- 设置页加载、保存、错误 Toast 与结构化日志：`LoomX/ViewModels/SettingsViewModel.cs:56,134,173-185,217-220,253-285`。
- 设置 UI 和四套资源：`LoomX/Views/SettingsView.axaml:22` 与 `LoomX/Resources/Strings*.resx`。
- 场景覆盖：`WindowsStartupServiceTests`、`SettingsViewContractTests`、CUA 设置页截图。

### 2. 概览页操作持久化网关运行意图

- 单字段更新链路：`LoomX/Configuration/ConfigurationManagementService.cs:107-115`、`LoomX/Services/ConfigSnapshotService.cs:117`、`LoomX/Services/AppDataStore.cs:130-140`。
- `AppSettingsInput` 不包含 `GatewayRunning`，普通设置保存不会改写运行意图：`LoomX/Configuration/ConfigurationManagementService.cs:8-9,79-96`。
- 概览页启动、关闭和切换分支先写意图再操作网关：`LoomX/ViewModels/MainWindowViewModel.cs:471-505`。
- 场景覆盖：`ConfigurationManagementServiceTests`、`AppDataStoreTests`、`OverviewGraphContractTests`。

### 3. APP 启动时恢复网关运行意图

- 启动协调器仅在 `GatewayRunning = true` 时调用网关启动委托，失败不写回意图：`LoomX/Services/ApplicationStartupCoordinator.cs:6-44`。
- 数据初始化成功后使用首个 Server URL，缺省地址为 `http://127.0.0.1:11434`：`LoomX/ViewModels/MainWindowViewModel.cs:225-244`。
- APP 创建真实 `WindowsStartupService` 并注入同一个实例：`LoomX/App.axaml.cs:139-159`。
- 场景覆盖：`ApplicationStartupCoordinatorTests`、`AppStartupAndProviderRefreshContractTests`、CUA 重启恢复。

### 4. APP 退出不覆盖网关运行意图

- 退出路径直接调用 `GatewayProcessService.StopAsync()`，未调用 `SetGatewayRunningAsync`：`LoomX/App.axaml.cs:163-186`。
- 场景覆盖：`AppStartupAndProviderRefreshContractTests` 与 CUA 关闭、重启流程。

## 自动化与构建证据

- 相关测试：88 个通过，0 个失败。
- `UpdateExperienceContractTests.更新安装请求复用正常退出路径和共享服务`：通过。
- Release 构建：0 个错误；仅保留仓库既有的 `NU1903`、`CS8618`、`CA2024` 警告。
- 完整测试：共 1276 个，1274 个通过；剩余 2 个失败均来自相对基线未修改的既有契约：
  - `GatewayViewContractTests.EndpointListUsesNonSelectingDisplayContainer`
  - `MainWindowNavigationContractTests.NavigationUsesPersistentActiveStateAndAnimatedSharedSelection`
- 已确认上述两项失败涉及的生产文件和测试文件相对基线 `ef5d3c06af011bf6e9c58d61e76d1d74457a6cb6` 均无改动。

## 发布包

- 目录：`outputs/LoomX-service-autostart-2026-09-23-2003/`。
- 目标：Release、win-x64、self-contained、非单文件。
- 共 446 个文件，唯一可执行文件为 `LoomX.exe`。
- `plugins/`、en-US、ja-JP、zh-TW 本地化资源存在；zh-CN 使用主程序集中的中性资源。

## CUA 实机验收

使用 `cua-driver` 后台操作本次发布包，截图仅包含 Loom-X 窗口。应用通过 `Start-Process -WindowStyle Hidden` 启动，并校验实际进程路径。

1. 设置页显示“开机时启动 Loom-X”开关及说明，截图：`settings-start-with-windows.png`。
2. 初始配置状态为 `StartWithWindows = 0`、`GatewayRunning = 0`。
3. 概览页点击“启动网关”后显示“运行中”和“停止网关”，配置状态变为 `(0, 1)`。
4. 直接关闭 APP 后进程退出、11434 端口释放，配置状态仍为 `(0, 1)`。
5. 再次运行同一发布包且不点击启动按钮，网关自动恢复为“运行中”，截图：`gateway-restored-after-restart.png`。
6. 验收结束后关闭测试进程，使用 SQLite 在线备份恢复配置库和活动库，并恢复 Run 注册表值；最终状态回到 `(0, 0)`，Run 值不存在。
7. 验收前已存在的另一个 Loom-X 进程未被关闭，验收结束后仍保持运行。

## 问题分级

### CRITICAL

- 无。

### WARNING

- 无本 change 实现警告。完整测试的 2 个基线契约失败不涉及本次变更文件。

### SUGGESTION

- 无。
