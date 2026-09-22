using System.Text.Json;
using System.Text.Json.Serialization;
using LoomX.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>
/// CLI 版本缓存。SQLite 存储于 LoomX.db 的 CliVersionEntries 表，TTL 24h。
/// 用户覆盖（Grok 手改）Source=UserOverridden 不受 TTL 影响。
/// 首次使用时若发现旧 JSON 文件（%LocalAppData%/LoomX/cli-versions.json），
/// 一次性迁移到数据库并删除旧文件；DB 不可用时静默降级为空缓存。
/// </summary>
public sealed class CliVersionCache
{
    public const string LegacyJsonFileName = "cli-versions.json";
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);
    private static readonly object Gate = new();

    private readonly IDbContextFactory<ConfigurationDbContext> _dbContextFactory;
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILogger<CliVersionCache> _logger;
    private bool _ensureSchemaAttempted;

    public CliVersionCache(
        IDbContextFactory<ConfigurationDbContext>? dbContextFactory = null,
        TimeSpan? ttl = null,
        Func<DateTimeOffset>? now = null,
        ILogger<CliVersionCache>? logger = null)
    {
        _dbContextFactory = dbContextFactory ?? DefaultDbContextFactory;
        _ttl = ttl ?? DefaultTtl;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CliVersionCache>.Instance;
    }

    /// <summary>测试专用：用 SQLite 连接串构造隔离的数据库实例。</summary>
    public CliVersionCache(
        string connectionString,
        TimeSpan? ttl = null,
        Func<DateTimeOffset>? now = null,
        ILogger<CliVersionCache>? logger = null)
        : this(
            new TestDbContextFactory(connectionString),
            ttl,
            now,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CliVersionCache>.Instance)
    {
    }
    internal TimeSpan Ttl => _ttl;

    /// <summary>
    /// 读取缓存中指定 CLI 的当前条目。不存在返回 null。
    /// </summary>
    public CliVersionInfo? Get(CliIdentityType type)
    {
        EnsureSchemaAndMigrate();
        lock (Gate)
        {
            var entry = LoadEntry(type);
            if (entry is null || string.IsNullOrWhiteSpace(entry.Version)) return null;
            return entry;
        }
    }

    /// <summary>
    /// 判断缓存条目是否过期或缺失。UserOverridden 不受 TTL 影响。
    /// </summary>
    public bool IsStale(CliIdentityType type)
    {
        var entry = Get(type);
        if (entry is null) return true;
        if (entry.Source is CliVersionSource.UserOverridden) return false;
        if (entry.FetchedAt is null) return true;
        return (_now() - entry.FetchedAt.Value) > _ttl;
    }

    /// <summary>
    /// 写入缓存条目。已存在的 UserOverridden 条目不会被非覆盖来源覆盖。
    /// </summary>
    public void Set(CliIdentityType type, string version, CliVersionSource source, DateTimeOffset? fetchedAt = null)
    {
        EnsureSchemaAndMigrate();
        lock (Gate)
        {
            var entries = LoadAll();
            if (entries.TryGetValue(type, out var existing)
                && existing.Source == CliVersionSource.UserOverridden
                && source != CliVersionSource.UserOverridden)
            {
                return;
            }
            entries[type] = new CachedEntry
            {
                Version = version,
                FetchedAt = fetchedAt ?? _now(),
                Source = source,
            };
            SaveAll(entries);
        }
    }

    /// <summary>设置用户覆盖版本（Grok 手改），Source 固定 UserOverridden。</summary>
    public void SetUserOverride(CliIdentityType type, string version)
        => Set(type, version, CliVersionSource.UserOverridden, fetchedAt: _now());

    /// <summary>判断指定 CLI 是否存在用户覆盖。</summary>
    public bool HasUserOverride(CliIdentityType type)
    {
        var entry = Get(type);
        return entry is not null && entry.Source == CliVersionSource.UserOverridden;
    }

    /// <summary>清除用户覆盖（允许回归默认值）。</summary>
    public void ClearUserOverride(CliIdentityType type)
    {
        EnsureSchemaAndMigrate();
        lock (Gate)
        {
            var entries = LoadAll();
            if (!entries.TryGetValue(type, out var entry) || entry.Source != CliVersionSource.UserOverridden)
                return;

            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var existing = db.CliVersionEntries.Find(type.ToString());
                if (existing is not null)
                {
                    db.CliVersionEntries.Remove(existing);
                    db.SaveChanges();
                }
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "CLI 版本用户覆盖清除失败，静默降级。");
            }
        }
    }

    /// <summary>
    /// 若旧 JSON 缓存文件仍存在，一次性迁移到数据库。迁移成功后删除旧文件。
    /// 数据库不可用或迁移失败时静默降级，不影响主流程。
    /// </summary>
    public void MigrateLegacyFromJson(string? legacyPath = null)
    {
        var path = legacyPath ?? DefaultLegacyPath;
        if (!File.Exists(path)) return;
        EnsureSchemaAndMigrate();
        lock (Gate)
        {
            var dbEntries = LoadAll();
            var jsonEntries = ReadLegacyJson(path);
            if (jsonEntries is null)
            {
                // 文件损坏或不可解析，保留原文件不覆盖。
                _logger.LogDebug("旧 CLI 版本 JSON 文件无法解析，保留原文件：{Path}", path);
                return;
            }
            if (jsonEntries.Count == 0)
            {
                // 空对象：迁移无内容，删除旧文件即可。
                TryDeleteLegacyFile(path);
                return;
            }
            foreach (var (type, entry) in jsonEntries)
            {
                if (dbEntries.TryGetValue(type, out var existing) && existing.Source == CliVersionSource.UserOverridden)
                    continue; // 不覆盖用户覆盖
                dbEntries[type] = entry;
            }
            try
            {
                SaveAll(dbEntries);
                TryDeleteLegacyFile(path);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "旧 CLI 版本 JSON 迁移失败，保留原文件。");
            }
        }
    }

    private void EnsureSchemaAndMigrate()
    {
        if (_ensureSchemaAttempted) return;
        lock (Gate)
        {
            if (_ensureSchemaAttempted) return;
            EnsureSchemaAndMigrateInternal();
            _ensureSchemaAttempted = true;
        }
    }

    private void EnsureSchemaAndMigrateInternal()
    {
        try
        {
            // SQLite 不会自动创建父目录；通过 DbContext.Database.GetDbConnection 的 DataSource 提前创建目录。
            using (var dirCheck = _dbContextFactory.CreateDbContext())
            {
                var conn = dirCheck.Database.GetDbConnection();
                var dataSource = conn.DataSource;
                if (!string.IsNullOrEmpty(dataSource)
                    && !dataSource.StartsWith(":memory:", StringComparison.OrdinalIgnoreCase))
                {
                    var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
                    if (!string.IsNullOrEmpty(dir))
                    {
                        try { Directory.CreateDirectory(dir); } catch { }
                    }
                }
            }
            using var db = _dbContextFactory.CreateDbContext();
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS CliVersionEntries (
                    CliType TEXT NOT NULL CONSTRAINT PK_CliVersionEntries PRIMARY KEY,
                    Version TEXT NOT NULL,
                    FetchedAt TEXT NULL,
                    Source TEXT NOT NULL DEFAULT 'Default'
                )
                """);
            MigrateLegacyFromJson();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "CLI 版本缓存表初始化失败，退化为空缓存。");
        }
    }

    private CliVersionInfo? LoadEntry(CliIdentityType type)
    {
        var entry = LoadAll().TryGetValue(type, out var e) ? e : null;
        if (entry is null || string.IsNullOrWhiteSpace(entry.Version)) return null;
        return new CliVersionInfo(type, entry.Version, entry.FetchedAt, entry.Source);
    }

    private Dictionary<CliIdentityType, CachedEntry> LoadAll()
    {
        try
        {
            using var db = _dbContextFactory.CreateDbContext();
            var result = new Dictionary<CliIdentityType, CachedEntry>();
            foreach (var entity in db.CliVersionEntries.AsNoTracking())
            {
                if (Enum.TryParse<CliIdentityType>(entity.CliType, ignoreCase: true, out var type)
                    && Enum.TryParse<CliVersionSource>(entity.Source, ignoreCase: true, out var source))
                {
                    DateTimeOffset? fetchedAt = DateTimeOffset.TryParse(entity.FetchedAt, out var at) ? at : null;
                    result[type] = new CachedEntry
                    {
                        Version = entity.Version,
                        FetchedAt = fetchedAt,
                        Source = source,
                    };
                }
            }
            return result;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "CLI 版本缓存读取失败，退化为空缓存。");
            return new Dictionary<CliIdentityType, CachedEntry>();
        }
    }

    private void SaveAll(Dictionary<CliIdentityType, CachedEntry> entries)
    {
        try
        {
            using var db = _dbContextFactory.CreateDbContext();
            foreach (var (type, entry) in entries)
            {
                var existing = db.CliVersionEntries.Find(type.ToString());
                if (existing is null)
                {
                    db.CliVersionEntries.Add(new CliVersionEntryEntity
                    {
                        CliType = type.ToString(),
                        Version = entry.Version,
                        FetchedAt = entry.FetchedAt?.ToString("O"),
                        Source = entry.Source.ToString(),
                    });
                }
                else
                {
                    existing.Version = entry.Version;
                    existing.FetchedAt = entry.FetchedAt?.ToString("O");
                    existing.Source = entry.Source.ToString();
                }
            }
            db.SaveChanges();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "CLI 版本缓存写入失败，静默降级。");
        }
    }

    private static Dictionary<CliIdentityType, CachedEntry>? ReadLegacyJson(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return null;
            var payload = JsonSerializer.Deserialize<CachedFilePayload>(text, LegacyJsonOptions);
            if (payload?.Entries is null || payload.Entries.Count == 0) return null;
            var result = new Dictionary<CliIdentityType, CachedEntry>();
            foreach (var (type, entry) in payload.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Version)) continue;
                result[type] = new CachedEntry
                {
                    Version = entry.Version,
                    FetchedAt = entry.FetchedAt,
                    Source = entry.Source,
                };
            }
            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteLegacyFile(string path)
    {
        try { File.Delete(path); }
        catch { /* 旧文件删除失败不影响主流程 */ }
    }

    private static string DefaultLegacyPath => System.IO.Path.Combine(AppDataPaths.RootDirectory, LegacyJsonFileName);

    private static IDbContextFactory<ConfigurationDbContext> DefaultDbContextFactory =>
        new DefaultFactory();

    private sealed class DefaultFactory : IDbContextFactory<ConfigurationDbContext>
    {
        public ConfigurationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
                .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = AppDataPaths.DatabasePath,
                    Pooling = true,
                }.ToString())
                .Options;
            return new ConfigurationDbContext(options);
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<ConfigurationDbContext>
    {
        private readonly string _connectionString;
        public TestDbContextFactory(string connectionString) => _connectionString = connectionString;
        public ConfigurationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
                .UseSqlite(_connectionString)
                .Options;
            return new ConfigurationDbContext(options);
        }
    }

    private static readonly JsonSerializerOptions LegacyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class CachedFilePayload
    {
        [JsonPropertyName("entries")]
        public Dictionary<CliIdentityType, CachedEntry>? Entries { get; set; }
    }

    private sealed class CachedEntry
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";
        [JsonPropertyName("fetchedAt")]
        public DateTimeOffset? FetchedAt { get; set; }
        [JsonPropertyName("source")]
        public CliVersionSource Source { get; set; } = CliVersionSource.Default;
    }
}
