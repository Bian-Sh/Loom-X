## Context

LoomX 已将运行时配置库和活动库固定到 `%LOCALAPPDATA%\LoomX\LoomX.db` 与 `%LOCALAPPDATA%\LoomX\LoomX.Activity.db`。但当前 `ApplicationDataMigration` 仍在桌面端和 Host 启动时执行，会查找 `%LOCALAPPDATA%\OllamaHub\OllamaHub.db` / `Activity.db`，执行 `VACUUM INTO`、完整性检查、临时文件清理和原子移动；当目标已存在时会产生用户可见的“目标库已存在，跳过旧库迁移”日志。用户明确要求这套数据库迁移能力不再保留。

## Goals / Non-Goals

**Goals:**

- 默认启动不再检查、读取、复制或迁移 `%LOCALAPPDATA%\OllamaHub` 下的数据库。
- 运行时只通过 `AppDataPaths.DatabasePath`、`AppDataPaths.ActivityDatabasePath` 等当前路径初始化数据库。
- 删除数据库迁移实现、迁移锁、旧路径常量、迁移专用异常、迁移测试和过时的升级说明。
- 保留本次已完成的无意义控制台日志降级：目标启动回退和透明外观诊断仅使用 `Debug`。
- 保留其他不属于数据库迁移的旧 JSON 偏好/缓存迁移逻辑。

**Non-Goals:**

- 不删除用户机器上已有的 `%LOCALAPPDATA%\OllamaHub` 文件；本次只删除应用内自动迁移能力。
- 不改变配置数据库 schema、活动数据库 schema、数据库初始化、日志基础设施或现有 API。
- 不修改 AssistantPreferences、CliVersionCache 等其他数据迁移功能。
- 不改变 Shell 启动回退、单实例、透明外观或网关行为。

## Decisions

- 直接删除 `ApplicationDataMigration.cs`，而不是保留空实现或弃用包装层，避免未来继续误触发旧库扫描和“迁移已跳过”日志。
- 从 `App` 和 `LoomXHost.CreateAsync` 删除迁移调用，使 Host 在 `AppDataPaths.EnsureCreated()` 后直接初始化当前配置数据库；桌面端仍在创建 `ConfigSnapshotService` 时使用同一目标路径。
- 从 `AppDataPaths` 删除 `LegacyRootDirectory`、`LegacyDatabasePath`、`LegacyActivityDatabasePath` 和 `DataMigrationLockPath`，避免旧路径以公共运行时路径形式继续存在。
- 通过修改 `app-data-migration` delta spec 删除旧库迁移、安全复制、幂等冲突处理和迁移失败保留等已废弃要求，仅保留当前 LoomX 数据路径和新安装行为。
- 测试采用 TDD：先把迁移移除后的负向契约写入 `AppDataPathsTests`、`LoomXHostTests` 和品牌/启动契约测试并确认失败，再删除实现和迁移测试，最后执行完整测试与发布验证。

## Data Flow

1. 进程启动调用 `AppDataPaths.EnsureCreated()`，只创建 `%LOCALAPPDATA%\LoomX` 和 `logs`。
2. `LoomXHost.CreateAsync` 使用 `AppDataPaths.DatabasePath` 创建配置数据库连接并执行现有 `ConfigurationDatabase.InitializeAsync`。
3. 桌面端直接创建 `ConfigSnapshotService`、`AppDataStore` 和活动服务；活动服务继续使用 `AppDataPaths.ActivityDatabasePath`。
4. `%LOCALAPPDATA%\OllamaHub` 不在任何正常启动数据流中读取；其已有文件保持原样。

## Error Handling

- 当前数据库初始化、读取和 schema 错误继续由既有配置数据库/服务日志与启动错误路径处理。
- 删除迁移后，不再捕获或包装旧库快照、校验、提交和迁移锁异常。
- 不为旧目录创建空数据库，也不删除旧目录；应用只对当前 LoomX 目录执行已有初始化逻辑。

## Testing Strategy

- 负向源码契约：验证启动入口不存在 `ApplicationDataMigration`、`EnsureMigratedAsync`、旧 OllamaHub 数据库路径和迁移锁引用。
- 路径契约：验证 `AppDataPaths` 只暴露 LoomX 当前运行时路径。
- 回归测试：保留现有配置初始化、Host 路由、透明外观和日志级别测试。
- 全量验证：运行 `dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore`、Release 构建和 `scripts/publish-desktop.ps1` 发布。

## Risks / Trade-offs

- [Risk] 用户无法通过 LoomX 自动接续旧 OllamaHub 数据 → Mitigation：这是本次明确的产品取舍；旧目录不被删除，用户仍可由旧版本读取或自行处理。
- [Risk] 删除迁移类型导致遗留调用或测试编译失败 → Mitigation：先用负向契约锁定所有入口，再用全量测试和 codegraph 查询确认引用清零。
- [Risk] 历史归档文档仍包含迁移描述 → Mitigation：只更新当前主规格和当前升级说明，保留历史 change 归档作为历史记录，不将其当作运行时契约。