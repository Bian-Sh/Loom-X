---
comet_change: suppress-meaningless-console-logs
role: technical-design
canonical_spec: openspec
archived-with: 2026-09-17-suppress-meaningless-console-logs
status: final
---

# 清理无意义控制台日志并移除旧数据库迁移设计

## 目标

LoomX 只使用 `%LOCALAPPDATA%\LoomX` 下的当前配置库、活动库和日志目录。默认启动不再扫描或迁移 `%LOCALAPPDATA%\OllamaHub`；同时，已识别的 Shell 回退和透明外观高频诊断继续保留为 `Debug`，不进入默认 `Information` 控制台。

## 组件变更

### 启动入口

- `LoomX/App.axaml.cs` 删除桌面端 `ApplicationDataMigration` 的同步调用。
- `LoomX/LoomXHost.cs` 删除 Host 创建阶段的迁移 logger factory 和 `EnsureMigratedAsync` 调用。
- 两个入口继续调用 `AppDataPaths.EnsureCreated()` / 使用 `AppDataPaths.DatabasePath`，由既有 `ConfigurationDatabase.InitializeAsync` 负责目标库初始化。

### 运行时路径

- `LoomX/AppDataPaths.cs` 保留 `RootDirectory`、`DatabasePath`、`ActivityDatabasePath`、`LogDirectory`、`ConfigurationInitializationLockPath`。
- 删除 `LegacyRootDirectory`、`LegacyDatabasePath`、`LegacyActivityDatabasePath` 和 `DataMigrationLockPath`。
- 不删除用户机器上的旧目录和文件；代码不再引用它们。

### 迁移实现与测试

- 删除 `LoomX/ApplicationDataMigration.cs`。
- 删除 `LoomX.Tests/ApplicationDataMigrationTests.cs`。
- 更新路径、Host、启动和品牌契约测试，以负向断言固定“无旧库迁移入口”。
- 保留 `AssistantPreferences`、`CliVersionCache` 等与数据库迁移无关的 JSON 兼容逻辑。

### 日志

- `App.axaml.cs` 的 Shell 子进程失败回退使用 `LogDebug`。
- `MainWindow.axaml.cs` 的透明外观开始/完成日志使用 `LogDebug`。
- 不修改 `LoggingBootstrap` 的最低级别和其他业务日志。

## 数据流

```text
AppDataPaths.EnsureCreated()
        ↓
App / LoomXHost 直接使用 AppDataPaths.DatabasePath
        ↓
ConfigurationDatabase.InitializeAsync
        ↓
ConfigSnapshotService / AppDataStore / ActivityStore
```

`%LOCALAPPDATA%\OllamaHub` 不在该数据流中出现。旧目录不会被删除、打开或迁移。

## 错误处理

删除旧库迁移后，不再产生快照、完整性检查、临时文件、迁移锁或迁移专用异常。当前数据库初始化失败仍沿用既有初始化与启动错误处理；不得为兼容旧库重新加入隐式 fallback。

## 测试策略

1. 先新增负向源码契约，运行后确认当前迁移实现使其失败。
2. 删除迁移实现与调用，并更新现有路径/启动契约；迁移专用测试随实现一并删除。
3. 运行目标测试，再运行完整 Release 测试（预期 672 个基线测试数量会因删除迁移测试而下降，必须报告实际通过数）。
4. 运行 Release 构建和 `scripts/publish-desktop.ps1`，确认发布入口仍为唯一 `LoomX.exe`。
5. 使用 `rg` 和 CodeGraph 查询确认 `ApplicationDataMigration`、`EnsureMigratedAsync`、`LegacyDatabasePath` 等符号无生产引用。

## 风险与取舍

- 不再自动迁移旧数据是明确的兼容性取舍；旧文件由应用保持不触碰，用户可使用旧版本或外部工具处理。
- 历史归档文档可能仍提及迁移，这是历史记录，不代表当前运行时行为；当前主规格和升级说明必须与新行为一致。
