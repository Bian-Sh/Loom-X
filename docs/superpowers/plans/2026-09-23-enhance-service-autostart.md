---
change: enhance-service-autostart
design-doc: openspec/changes/enhance-service-autostart/design.md
base-ref: ef5d3c06af011bf6e9c58d61e76d1d74457a6cb6
---

<!-- comet-task-authority: openspec/changes/enhance-service-autostart/tasks.md -->

# 服务自启动加强实施计划

## 实施顺序

### 配置模型与网关意图

<!-- comet-task-ref:61ae2fad-ead3-41a1-b7cc-193caa84eb59 -->
先在配置服务测试中声明两个布尔字段的默认值、完整设置读写和 SQLite 结构要求，确认测试因字段缺失而失败；随后贯通实体、输入/响应、运行时快照、schema ready 检查和表结构初始化。验证命令：`dotnet test LoomX.Tests --filter FullyQualifiedName~ConfigurationManagementServiceTests|FullyQualifiedName~ConfigurationDatabaseMigrationTests`。

<!-- comet-task-ref:fd3c2073-7d4c-4aff-a064-0c682060bdfe -->
为网关意图增加单字段持久化测试，确认其他设置不变化；实现管理服务、桌面配置服务和 AppDataStore 快照更新链路。验证命令：`dotnet test LoomX.Tests --filter FullyQualifiedName~AppDataStoreTests|FullyQualifiedName~ConfigurationManagementServiceTests`。

### Windows 自启动与设置 UI

<!-- comet-task-ref:4a8b1ea2-6c7f-4ad7-b876-1b136cb353ff -->
通过可注入注册表存储或等价测试边界先验证启用、移除和带引号命令格式，再实现当前用户 Run 注册服务；不得写入机器级注册表。验证命令：运行新增 Windows 自启动服务测试。

<!-- comet-task-ref:2c4f308e-0387-44f8-b40d-7a6301554781 -->
将 `StartWithWindows` 接入 SettingsViewModel 的加载、自动保存及注册同步，更新 SettingsView 和四套资源；验证本地化覆盖率、无硬编码 CJK 和视图绑定测试。

### 启停意图与启动恢复

<!-- comet-task-ref:cb666a6e-975b-45c2-a2a6-f5c755dcdac2 -->
在概览页契约测试中先证明启动/关闭用户操作必须写入意图，然后让 Start、Stop 和 Toggle 的两个分支先持久化再操作网关；状态事件和刷新路径不得写入。

<!-- comet-task-ref:1059e916-c96e-41f8-a33f-e27937c5f03a -->
为初始化恢复建立可测试的协调边界：配置加载成功后校准 Windows 自启动，并仅在 `GatewayRunning` 为 true 时启动网关。退出路径保持直接 StopAsync，测试确认无意图清零调用。

### 验证与发布

<!-- comet-task-ref:264fec92-b221-4d13-978c-6e3c7a466f67 -->
依次运行 OpenSpec strict validate、相关测试、完整测试和 Release 构建，修复范围内失败并复验。

<!-- comet-task-ref:96e9d929-22e5-4308-8c4a-ddf65411dd8f -->
隐藏启动发布包，使用 CUA 只截取 APP 窗口验收设置开关、概览页启停和重启恢复；测试后恢复用户自启动注册表状态，避免污染主机环境。

<!-- comet-task-ref:0addce73-e868-4200-883d-44c9a096c8a3 -->
执行 win-x64 Release publish，把产物复制到 `outputs/LoomX-service-autostart-2026-09-23-HHmm`，检查 `LoomX.exe`、插件和本地化资源齐全。

## 约束

- 只修改独立 worktree，不触碰主工作区的其他会话改动。
- 全程遵循 TDD；每项生产代码之前保留对应 RED 证据。
- 运行时配置继续统一通过 `AppDataPaths` 使用 `%LOCALAPPDATA%\LoomX\LoomX.db`。
- Windows 自启动仅使用 HKCU，失败不得阻止 APP 运行。
- `GatewayRunning` 只由概览页用户操作写入，自动恢复和退出清理不得写入。
