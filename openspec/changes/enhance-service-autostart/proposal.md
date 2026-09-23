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
