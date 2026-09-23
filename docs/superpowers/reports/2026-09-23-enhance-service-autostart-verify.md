# 服务自启动加强验证报告

## 验证范围

- Windows 开机自启动设置与当前用户 Run 注册表同步。
- 概览页启动/停止网关时持久化 `GatewayRunning`。
- APP 退出只释放网关资源，不清零运行意图。
- APP 再次运行时按退出前的网关运行意图恢复服务。

## 自动化验证

- `comet classic openspec -- validate enhance-service-autostart --strict`：通过。
- 相关测试：88 个通过，0 个失败。
- `UpdateExperienceContractTests.更新安装请求复用正常退出路径和共享服务`：通过。
- `dotnet build LoomX/LoomX.csproj -c Release`：通过，0 个错误；仅保留仓库既有的 `NU1903`、`CS8618`、`CA2024` 警告。
- 完整测试：共 1276 个，1274 个通过；剩余 2 个失败均来自相对基线未修改的既有契约：
  - `GatewayViewContractTests.EndpointListUsesNonSelectingDisplayContainer`
  - `MainWindowNavigationContractTests.NavigationUsesPersistentActiveStateAndAnimatedSharedSelection`
- 已通过 `git diff --exit-code ef5d3c06af011bf6e9c58d61e76d1d74457a6cb6 -- ...` 确认上述两项失败涉及的生产文件和测试文件相对本 change 基线均无改动。

## 发布包

- 发布目录：`outputs/LoomX-service-autostart-2026-09-23-2003/`。
- 发布目标：Release、win-x64、self-contained、非单文件。
- 产物共 446 个文件，唯一可执行文件为 `LoomX.exe`。
- `plugins/`、en-US、ja-JP、zh-TW 本地化资源存在；zh-CN 使用主程序集中的中性资源。

## CUA 验收

使用 `cua-driver` 对发布包进行后台窗口操作与目标窗口截图，未截取全屏。启动命令通过 `Start-Process -WindowStyle Hidden` 执行，并校验进程路径指向本次发布包。

1. 设置页显示“开机时启动 Loom-X”开关及说明，截图：`settings-start-with-windows.png`。
2. 初始数据库状态为 `StartWithWindows = 0`、`GatewayRunning = 0`。
3. 在概览页点击“启动网关”后，界面显示“运行中”和“停止网关”，数据库状态变为 `(0, 1)`。
4. 直接关闭 APP 后，进程退出且 11434 端口释放，数据库状态仍为 `(0, 1)`。
5. 再次运行同一发布包，未点击启动按钮，网关自动恢复为“运行中”，截图：`gateway-restored-after-restart.png`。
6. 验收结束后关闭测试进程，使用 SQLite 在线备份恢复配置库和活动库，并恢复 Run 注册表值；最终状态回到 `(0, 0)`，Run 值不存在。
7. 验收前已存在的另一个 Loom-X 进程未被关闭，验收结束后仍保持运行。

## 结论

本 change 新增行为已通过相关自动化测试、Release 构建、发布包检查和 CUA 实机验收。完整测试中的 2 个失败为基线既有契约失败，不涉及本次修改。
