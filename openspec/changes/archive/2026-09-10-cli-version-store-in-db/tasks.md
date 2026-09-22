# 任务清单

## 1. DB schema

- [x] 在 `ConfigurationDbContext` 添加 `DbSet<CliVersionEntryEntity> CliVersionEntries`
- [x] 新增 `CliVersionEntryEntity` 实体类（`CliType` PK、`Version`、`FetchedAt`、`Source`）
- [x] `OnModelCreating` 配置实体约束（`CliType` max 32 required、`Version` max 128 required、`Source` max 32 required）
- [x] `EnsureSchemaAsync` 添加 `CREATE TABLE IF NOT EXISTS CliVersionEntries`

## 2. CliVersionCache SQLite 化

- [x] 完全重写 `LoomX/Services/CliVersionCache.cs` 为 SQLite 后端
- [x] 构造函数支持 `IDbContextFactory` 与 `connectionString` 两种注入方式
- [x] 保留旧 `new CliVersionCache()` 无参调用（生产代码 5 处不改动）
- [x] 保留 public API：`Get` / `Set` / `IsStale` / `SetUserOverride` / `HasUserOverride` / `ClearUserOverride`
- [x] 用户覆盖条目不受 TTL 影响
- [x] `ClearUserOverride` 走 `DbSet.Remove` 显式删除行
- [x] 新增 `MigrateLegacyFromJson(legacyPath)`：DB 优先、不覆盖用户覆盖、坏 JSON 保留原文件
- [x] DB 不可用/写入失败静默降级（catch + LogDebug）
- [x] 首次建表前 `Directory.CreateDirectory` 确保父目录存在
- [x] JSON 反序列化启用 `JsonStringEnumConverter`

## 3. 测试改造

- [x] `CliVersionCacheTests` 全部改为 SQLite 连接串构造
- [x] `CliVersionServiceTests` 全部改为 SQLite 连接串构造
- [x] 新增 `MigrateLegacyFromJson_CopiesEntriesAndDeletesSource`
- [x] 新增 `MigrateLegacyFromJson_DoesNotOverwriteUserOverride`
- [x] 新增 `MigrateLegacyFromJson_InvalidJsonKeepsFile`
- [x] 新增 `Get_MissingLegacyFile_SkipsMigrationSilently`

## 4. 构建验证 + 提交

- [x] `dotnet build LoomX/LoomX.csproj` 0 error 0 warning（新增）
- [x] `dotnet test --filter "FullyQualifiedName~CliVersion"` 36/36 通过
- [x] `dotnet test`（全量）512/512 通过
