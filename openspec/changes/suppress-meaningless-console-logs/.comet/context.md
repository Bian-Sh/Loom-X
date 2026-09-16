# Comet Design Handoff

- Change: suppress-meaningless-console-logs
- Phase: design
- Mode: compact
- Context hash: 036b7668334c52bf1339b1ad21a942864b52957188038c365fd1e217d6e699c9

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/suppress-meaningless-console-logs/proposal.md

- Source: openspec/changes/suppress-meaningless-console-logs/proposal.md
- Lines: 1-24
- SHA256: 7a19bc878f8e33ffbff76eba51596c33d9ba6a1a748908ef9c93c826c04252f2

```md
## Why

桌面端当前会把两类不影响正常运行的诊断事件展示到用户可见控制台：Windows Shell 子进程启动失败后的正常回退，以及透明外观每次应用时的开始/完成明细。同时，启动流程仍保留从旧版 OllamaHub 数据目录复制数据库的迁移能力，会在目标库已存在时输出“跳过旧库迁移”信息。当前产品已固定使用 `%LOCALAPPDATA%\LoomX`，不再需要兼容旧库迁移。

## What Changes

- 将 Windows Shell 自启动子进程失败但继续当前进程的日志从 `Warning` 调整为 `Debug`。
- 将透明外观应用开始和完成日志从 `Information` 调整为 `Debug`。
- 删除旧版 OllamaHub 配置库和活动库到 LoomX 数据库的自动迁移逻辑、迁移锁、临时快照和迁移异常类型。
- 删除启动流程中的迁移调用；应用只初始化并使用 `%LOCALAPPDATA%\LoomX` 下的目标数据库。
- 删除仅用于旧库迁移的测试、路径常量和过时的升级说明，并更新 `app-data-migration` 规格。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `app-data-migration`: 保留 LoomX 当前数据路径约束，移除旧 OllamaHub 数据库自动迁移及其安全复制、冲突处理和失败回滚要求。

## Impact

影响 `LoomX/App.axaml.cs`、`LoomX/LoomXHost.cs`、`LoomX/AppDataPaths.cs`、`LoomX/ApplicationDataMigration.cs`、相关测试、`openspec/specs/app-data-migration/spec.md` 和 `docs/loomx-upgrade.md`。不新增 API；会删除仅供内部启动迁移使用的类型和路径属性。与助手偏好、CLI 缓存等其他 JSON 迁移无关的逻辑保持不变。
```

## openspec/changes/suppress-meaningless-console-logs/design.md

- Source: openspec/changes/suppress-meaningless-console-logs/design.md
- Lines: 1-53
- SHA256: 236d022ea58db0c7d0b927e44cc6f9c5b385105fe81a2180a20c53aaa3e19eda

```md
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
```

## openspec/changes/suppress-meaningless-console-logs/tasks.md

- Source: openspec/changes/suppress-meaningless-console-logs/tasks.md
- Lines: 1-10
- SHA256: 6e01208fd46cf6c5cefbcd7f1fb01d906e000afd7f09cc433552a810d22e6d8c

```md
## 1. 清理无意义控制台日志

- [x] 1.1 增加失败测试，验证 Shell 回退消息不再使用 `Warning`，透明外观消息不再使用 `Information`
- [x] 1.2 将目标启动回退和透明外观日志调整为 `Debug`，运行相关测试与构建确认行为不变

## 2. 删除旧数据库迁移能力

- [ ] 2.1 先增加负向回归测试，验证启动入口、Host、运行时路径和品牌契约不再引用 `ApplicationDataMigration`、`EnsureMigratedAsync`、旧 OllamaHub 数据库路径或迁移锁
- [ ] 2.2 删除 `ApplicationDataMigration` 及其异常、启动调用、旧路径属性和迁移专用测试，保持当前 LoomX 数据库初始化流程不变
- [ ] 2.3 更新 `app-data-migration` 主规格、升级说明和 Comet 设计/任务产物，确认其他 JSON 迁移逻辑不受影响
- [ ] 2.4 运行相关测试、完整测试、Release 构建和 win-x64 发布，检查日志中不再出现旧库迁移提示
```

## openspec/changes/suppress-meaningless-console-logs/specs/app-data-migration/spec.md

- Source: openspec/changes/suppress-meaningless-console-logs/specs/app-data-migration/spec.md
- Lines: 1-87
- SHA256: ec4943a0b27ba29cf5e256e449059e1b9a2a95a95bb86d9e99b87868fc9474c3

[TRUNCATED]

```md
## MODIFIED Requirements

### Requirement: LoomX runtime data paths

正常运行时 SHALL 只从 `AppDataPaths` 解析并使用以下路径：根目录 `%LOCALAPPDATA%\\LoomX`、配置库 `LoomX.db`、活动库 `LoomX.Activity.db`、日志目录 `logs` 和配置库初始化锁 `LoomX.db.init.lock`。运行时 SHALL 不解析、不读取、不写入或迁移 `%LOCALAPPDATA%\\OllamaHub` 下的数据库，也不得保留旧数据库迁移锁或旧数据库路径常量。

#### Scenario: New installation

- **WHEN** LoomX 在没有旧数据目录的用户环境中首次启动
- **THEN** 应用创建 `%LOCALAPPDATA%\\LoomX` 及其新数据库和日志目录，不创建或检查 `%LOCALAPPDATA%\\OllamaHub` 数据库

#### Scenario: Legacy directory is ignored

- **WHEN** 用户环境中仍存在 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db` 或 `Activity.db`
- **THEN** LoomX 不打开、不复制、不校验这些文件，直接按当前 LoomX 路径初始化和使用数据库

### Requirement: Legacy database migration

LoomX SHALL NOT 将 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db` 或 `Activity.db` 自动迁移到 LoomX 数据目录。LoomX SHALL 在 `AppDataPaths.EnsureCreated()` 后直接使用当前 LoomX 数据库路径执行现有初始化流程，不得在创建配置数据库连接前调用旧数据库迁移服务，也不得提供旧数据库自动迁移、快照、临时提交或迁移异常处理能力。

#### Scenario: First launch with legacy data

- **WHEN** 用户首次启动 LoomX 且旧配置库和活动库存在
- **THEN** LoomX 忽略旧配置库和活动库，只按当前 LoomX 路径初始化并继续运行

#### Scenario: Legacy configuration only

- **WHEN** 旧配置库存在但旧活动库不存在
- **THEN** LoomX 不迁移旧配置库，按正常初始化流程使用当前 LoomX 配置库和活动库

#### Scenario: Current database initialization

- **WHEN** LoomX 启动且 `%LOCALAPPDATA%\\LoomX\\LoomX.db` 尚不存在
- **THEN** LoomX 使用当前数据库初始化流程创建目标库，不搜索或复制旧 OllamaHub 数据库

#### Scenario: Existing current database

- **WHEN** `%LOCALAPPDATA%\\LoomX\\LoomX.db` 或 `%LOCALAPPDATA%\\LoomX\\LoomX.Activity.db` 已存在
- **THEN** LoomX 继续使用当前数据库，不输出旧库迁移跳过信息，也不访问旧数据库目录

## REMOVED Requirements

### Requirement: Safe SQLite copy and integrity validation

移除旧库 `VACUUM INTO` 快照、临时目标、完整性检查和原子提交流程。

#### Scenario: WAL database migration

- **WHEN** 旧数据库存在尚未合并到主文件的 WAL 数据
- **THEN** LoomX 不读取或迁移旧数据库

#### Scenario: Interrupted migration

- **WHEN** 迁移在临时文件阶段中断
- **THEN** LoomX 不再创建或清理旧库迁移临时文件

#### Scenario: Activity database retry after configuration success

- **WHEN** 配置库迁移已成功但活动库迁移失败
- **THEN** LoomX 不执行分阶段旧库迁移重试

### Requirement: Idempotent conflict handling

移除旧库迁移的目标冲突判断、重复迁移和旧库优先级处理。

#### Scenario: Relaunch after migration

- **WHEN** LoomX 已启动过且旧目录仍然存在
- **THEN** LoomX 只使用当前 LoomX 数据库，不检查旧目录

#### Scenario: New database already exists

- **WHEN** 新数据库存在而旧数据库也存在
- **THEN** LoomX 保持当前数据库不变且不访问旧数据库

### Requirement: Failure safety and legacy retention

移除旧库迁移失败、迁移锁、临时文件清理和迁移异常阻止启动等专用行为；旧目录保留属于应用不触碰旧目录的结果，不再是迁移流程的运行时保证。

#### Scenario: Corrupt legacy database

```

Full source: openspec/changes/suppress-meaningless-console-logs/specs/app-data-migration/spec.md
