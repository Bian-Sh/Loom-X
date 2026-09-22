# CLI 版本缓存从 JSON 文件迁移到 LoomX.db SQLite

## 背景

前置变更 `provider-cli-identity` 引入的 `CliVersionCache` 把三家 CLI 的版本缓存到 `%LocalAppData%/LoomX/cli-versions.json` 独立 JSON 文件（`LoomX/Services/CliVersionCache.cs`）。这个 JSON 文件：

- 与主业务库 `LoomX.db`（`LoomX/Configuration/ConfigurationDbContext.cs`）分离，两套持久化机制并存；
- 每次读写都要做完整文件 IO（`File.ReadAllText` + `File.WriteAllText`），无并发保护以外的事务保护；
- 用户覆盖（Grok 手改）与默认版本、拉取版本共用同一份文件，任何序列化失败都会一次性丢失全部缓存；
- 测试需要为每个用例单独生成独立 JSON 文件路径，测试间通过 `Guid` 隔离。

Comet tweak 目标：把存储迁到 `LoomX.db` 的 `CliVersionEntries` 表，保留 API 契约与旧 JSON 文件的一次性迁移。

## 目标

1. `CliVersionCache` 后端从 JSON 文件改为 `LoomX.db` 的 `CliVersionEntries` 表（一类型一行，PK = `CliIdentityType` 名字）。
2. 保留 `CliVersionCache` 的 public API：`Get` / `Set` / `IsStale` / `SetUserOverride` / `HasUserOverride` / `ClearUserOverride`；调用方（`CliVersionService`、`MainWindowViewModel`）无感知。
3. 首次使用时若发现旧 JSON 文件 `%LocalAppData%/LoomX/cli-versions.json`，一次性迁移到数据库后删除旧文件；迁移失败保留原文件不覆盖。
4. 用户覆盖（`CliVersionSource.UserOverridden`）不受 24h TTL 影响，且迁移时不允许被旧 JSON 覆盖。
5. DB 不可用/写入失败静默降级（不抛异常，log 一条 debug），保证用户操作不阻塞。
6. 构造函数支持 `connectionString` 与 `IDbContextFactory` 注入，便于测试用隔离 SQLite；旧的 5 处 `new CliVersionCache()` 无参调用保持不变。

## 范围

**包含**
- `LoomX/Configuration/ConfigurationDbContext.cs`：新增 `CliVersionEntryEntity` 实体 + `DbSet<CliVersionEntryEntity> CliVersionEntries` + `OnModelCreating` 配置 + `EnsureSchemaAsync` 中的 `CREATE TABLE IF NOT EXISTS CliVersionEntries`
- `LoomX/Services/CliVersionCache.cs`：完全重写为 SQLite 后端；构造函数兼容旧 `new CliVersionCache()` 无参调用；新增 `MigrateLegacyFromJson(string? legacyPath = null)` 一次性迁移路径
- `LoomX.Tests/Services/CliVersionCacheTests.cs`：全部用例改为 SQLite 连接串构造；新增迁移用例（复制条目 + 删除源文件 / 不覆盖用户覆盖 / 坏 JSON 保留源 / 缺表降级）
- `LoomX.Tests/Services/CliVersionServiceTests.cs`：`CliVersionCache` 构造改为连接串
- `openspec/changes/cli-version-store-in-db/{proposal.md,design.md,tasks.md}`：本文件集

**不包含**
- 不改 `CliVersionService` 的 public API
- 不改 `CliIdentityType` / `CliVersionSource` 枚举
- 不做数据库迁移脚本或版本化 schema（沿用 `EnsureSchemaAsync` 幂等建表方式）
- 不处理多用户/多 profile 场景
- 不处理 CLI 版本缓存的删除管理界面

## 非目标

- **不做** 旧 JSON 长期双写：迁移一次后完全切换到 SQLite，旧文件删除
- **不做** SQLite WAL / busy_timeout 调优（沿用项目默认配置）
- **不做** 用户覆盖过期机制（用户覆盖永不过期，与旧行为一致）
- **不做** 跨机器缓存同步（本变更只解决单机器存储位置）
- **不做** 缓存大小限制 / 清理（3 行数据，无需清理）

## 依赖

- `Microsoft.EntityFrameworkCore.Sqlite`（`LoomX.csproj` 已引用）
- `ConfigurationDbContext.EnsureSchemaAsync` 幂等建表机制（`LoomX/Configuration/ConfigurationDbContext.cs`）
- `AppDataPaths.DatabasePath` / `RootDirectory`（`LoomX/AppDataPaths.cs`）
- `AppDataPathResolver` 已建立的应用数据根目录（旧 JSON 路径基座）

## 风险

- **DB 不可用场景**：SQLite 文件锁/权限/磁盘满时 `SaveChanges` 抛异常。已通过 `try/catch + LogDebug` 静默降级为「保持旧值」。缓存写入失败只影响后续拉取是否重复，不影响当前展示
- **旧 JSON 迁移失败**：JSON 损坏或用户覆盖条目冲突。已按 `ReadLegacyJson` 返回 null → 保留原文件的策略处理；不覆盖用户覆盖条目
- **连接池行为**：`Pooling=true` 下 SQLite 会复用连接。测试用例通过临时文件路径隔离实例；生产路径 `LoomX.db` 单一实例，池由进程管理
- **枚举序列化**：System.Text.Json 默认不识别字符串枚举。已启用 `JsonStringEnumConverter` 保证旧 JSON 中 `"NpmRegistry"` 等键能正确解析
- **`SaveAll` 语义**：当前实现是「按传入字典 upsert」，不做删除。为避免 `ClearUserOverride` 需要删除行，单独走 `DbSet.Remove + SaveChanges` 路径

## 验收

1. `dotnet build LoomX.slnx` 零 error、无新增 warning
2. `dotnet test` 全绿（含新增迁移用例）
3. 首次运行 LoomX 时，若 `%LocalAppData%/LoomX/cli-versions.json` 存在，`LoomX.db` 中 `CliVersionEntries` 表出现对应行且旧 JSON 文件被删除
4. 手动改 `LoomX.db` 中某行 `Source = 'UserOverridden'` 后运行 app：Grok 版本显示该值且不刷新（24h 后仍有效）
5. 生产环境中 5 处 `new CliVersionCache()` 调用无修改（`LoomX/Services/CliVersionService.cs:36`、`LoomX/ViewModels/MainWindowViewModel.cs` 5 处）
