# 设计：CLI 版本缓存 SQLite 化

## 数据模型

`CliVersionEntries` 表（`LoomX.db`）：

| 列 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| `CliType` | TEXT | PK, NOT NULL, max 32 | `CliIdentityType` 枚举名（`ClaudeCode` / `Codex` / `Grok`） |
| `Version` | TEXT | NOT NULL, max 128 | 版本号字符串 |
| `FetchedAt` | TEXT | NULL | ISO 8601 (round-trip "O" 格式)；用户覆盖也为非空 |
| `Source` | TEXT | NOT NULL, DEFAULT 'Default', max 32 | `CliVersionSource` 枚举名 |

一类型一行，共 ≤ 3 行。主键使用 `CliIdentityType` 名字而非 int，避免不必要的自增开销，也方便 SQL 调试。

## API 契约

```csharp
public sealed class CliVersionCache
{
    public const string LegacyJsonFileName = "cli-versions.json";
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    // 主构造函数（默认走 LoomX.db）
    public CliVersionCache(
        IDbContextFactory<ConfigurationDbContext>? dbContextFactory = null,
        TimeSpan? ttl = null,
        Func<DateTimeOffset>? now = null,
        ILogger<CliVersionCache>? logger = null);

    // 测试专用（连接串隔离）
    public CliVersionCache(
        string connectionString,
        TimeSpan? ttl = null,
        Func<DateTimeOffset>? now = null,
        ILogger<CliVersionCache>? logger = null);

    public CliVersionInfo? Get(CliIdentityType type);
    public bool IsStale(CliIdentityType type);
    public void Set(CliIdentityType type, string version, CliVersionSource source, DateTimeOffset? fetchedAt = null);
    public void SetUserOverride(CliIdentityType type, string version);
    public bool HasUserOverride(CliIdentityType type);
    public void ClearUserOverride(CliIdentityType type);
    public void MigrateLegacyFromJson(string? legacyPath = null);
}
```

- `Get` 语义：不存在或 `Version` 为空 → 返回 null
- `IsStale` 语义：条目缺失 → true；`Source == UserOverridden` → false；`FetchedAt` 缺失 → true；否则比较 TTL
- `Set` 语义：若已存在 `Source == UserOverridden` 且新来源非 `UserOverridden` → 忽略（保护用户覆盖）
- `SetUserOverride` → 强制写入 `Source=UserOverridden, FetchedAt=now()`
- `ClearUserOverride` → 直接删除数据库行（`DbSet.Remove + SaveChanges`），避免用 `SaveAll` upsert 造成的假删除

## 存储路径

```
%LocalAppData%\LoomX\LoomX.db  ← 新增 CliVersionEntries 表
%LocalAppData%\LoomX\cli-versions.json  ← 旧 JSON，一次性迁移后删除
```

- 默认工厂 `DefaultDbContextFactory` 使用 `AppDataPaths.DatabasePath`，`Pooling=true`
- `EnsureSchemaAndMigrate()` 首次访问时确保表存在，并触发默认路径的 JSON 迁移

## 迁移流程

`MigrateLegacyFromJson(string? legacyPath = null)`：

1. 解析路径（默认 `%LocalAppData%\LoomX\cli-versions.json`）
2. 文件不存在 → 直接返回
3. `EnsureSchemaAndMigrate()` → 确保表存在（**顺序关键**：不先建表就 LoadAll 会静默吞掉数据）
4. `LoadAll()` → 读 DB 现有条目
5. `ReadLegacyJson(path)` → 解析 JSON；损坏/空 → 保留原文件不删除
6. 逐条合并：DB 中若 `Source == UserOverridden` 则跳过（用户覆盖优先）
7. `SaveAll(entries)` → upsert
8. `TryDeleteLegacyFile(path)` → 迁移成功删除；`SaveChanges` 抛异常时不删除

## 降级策略

| 场景 | 行为 |
| --- | --- |
| DB 打开失败 | 所有读写返回空/无操作，log debug |
| `SaveChanges` 抛异常 | `SaveAll` catch → log debug，保持旧值 |
| 旧 JSON 解析失败 | 保留原文件，log debug |
| SQLite 文件路径父目录不存在 | 首次写入前 `Directory.CreateDirectory`（SQLite 不自动建父目录） |

## 测试策略

- 每个 `CliVersionCacheTests` 用例：`Path.GetTempPath() + "loomx-tests" + Guid` 独立 `.db` 文件 + `Pooling=true`
- 迁移用例：写一个临时 JSON 文件，调用 `MigrateLegacyFromJson` 后验证 DB 内容 + 文件删除
- `CliVersionServiceTests`：`_connectionString` 替换旧 `_cachePath`；所有 `new CliVersionCache(...)` 参数改连接串

## 变更足迹

- `LoomX/Configuration/ConfigurationDbContext.cs`：+`DbSet<CliVersionEntryEntity> CliVersionEntries`、+实体类、+`OnModelCreating` 配置、+`EnsureSchemaAsync` 中 `CREATE TABLE IF NOT EXISTS CliVersionEntries`
- `LoomX/Services/CliVersionCache.cs`：完全重写（~390 行），保留旧 API
- `LoomX.Tests/Services/CliVersionCacheTests.cs`：全部改用 SQLite；+4 个迁移相关用例
- `LoomX.Tests/Services/CliVersionServiceTests.cs`：`_cachePath` → `_connectionString`
