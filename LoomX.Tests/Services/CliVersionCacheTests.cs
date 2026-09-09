using LoomX.Configuration;
using LoomX.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class CliVersionCacheTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public CliVersionCacheTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "loomx-tests", Guid.NewGuid().ToString("N") + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Pooling = true,
        }.ToString();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* 清理失败忽略 */ }
    }

    [Fact]
    public void IsStale_FreshCache_ReturnsFalse()
    {
        var cache = new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow);
        cache.Set(CliIdentityType.ClaudeCode, "2.1.263", CliVersionSource.NpmRegistry);

        Assert.False(cache.IsStale(CliIdentityType.ClaudeCode));
    }

    [Fact]
    public void IsStale_ExpiredCache_ReturnsTrue()
    {
        var baseTime = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var cache = new CliVersionCache(_connectionString, ttl: TimeSpan.FromHours(24), now: () => baseTime);
        cache.Set(CliIdentityType.ClaudeCode, "2.1.263", CliVersionSource.NpmRegistry);

        // 时间推进到超过 TTL；共享 SQLite 文件，新实例也能读到旧条目。
        var laterCache = new CliVersionCache(_connectionString, ttl: TimeSpan.FromHours(24), now: () => baseTime.AddHours(25));
        Assert.True(laterCache.IsStale(CliIdentityType.ClaudeCode));
    }

    [Fact]
    public void IsStale_MissingCache_ReturnsTrue()
    {
        var cache = new CliVersionCache(_connectionString);
        Assert.True(cache.IsStale(CliIdentityType.Grok));
    }

    [Fact]
    public void Get_ReturnsStoredVersion()
    {
        var cache = new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow);
        cache.Set(CliIdentityType.Codex, "0.153.4", CliVersionSource.GitHubReleases);

        var info = cache.Get(CliIdentityType.Codex);

        Assert.NotNull(info);
        Assert.Equal("0.153.4", info!.Version);
        Assert.Equal(CliVersionSource.GitHubReleases, info.Source);
        Assert.Equal(CliIdentityType.Codex, info.Type);
        Assert.NotNull(info.FetchedAt);
    }

    [Fact]
    public void SetUserOverride_MarksAsUserOverridden()
    {
        var cache = new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow);
        cache.SetUserOverride(CliIdentityType.Grok, "9.9.9");

        Assert.True(cache.HasUserOverride(CliIdentityType.Grok));
        var info = cache.Get(CliIdentityType.Grok)!;
        Assert.Equal("9.9.9", info.Version);
        Assert.Equal(CliVersionSource.UserOverridden, info.Source);
    }

    [Fact]
    public void HasUserOverride_FalseWhenNotSet()
    {
        var cache = new CliVersionCache(_connectionString);
        Assert.False(cache.HasUserOverride(CliIdentityType.Grok));
    }

    [Fact]
    public void IsStale_UserOverrideReturnsFalse()
    {
        var cache = new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow);
        cache.SetUserOverride(CliIdentityType.Grok, "9.9.9");

        // 用户覆盖不受 TTL 影响
        var laterCache = new CliVersionCache(_connectionString, ttl: TimeSpan.FromHours(24), now: () => DateTimeOffset.UtcNow.AddHours(36));
        Assert.False(laterCache.IsStale(CliIdentityType.Grok));
    }

    [Fact]
    public void SetUserOverride_OverwritesPreviousDefault()
    {
        var cache = new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow);
        cache.Set(CliIdentityType.Grok, "1.0.6", CliVersionSource.Default);
        Assert.False(cache.HasUserOverride(CliIdentityType.Grok));

        cache.SetUserOverride(CliIdentityType.Grok, "2.0.0");

        Assert.True(cache.HasUserOverride(CliIdentityType.Grok));
        Assert.Equal("2.0.0", cache.Get(CliIdentityType.Grok)!.Version);
    }

    [Fact]
    public void ClearUserOverride_RemovesOverride()
    {
        var cache = new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow);
        cache.SetUserOverride(CliIdentityType.Grok, "2.0.0");
        Assert.True(cache.HasUserOverride(CliIdentityType.Grok));

        cache.ClearUserOverride(CliIdentityType.Grok);

        Assert.False(cache.HasUserOverride(CliIdentityType.Grok));
    }

    [Fact]
    public void Set_PersistsToDatabase()
    {
        var cache = new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow);
        cache.Set(CliIdentityType.ClaudeCode, "2.1.263", CliVersionSource.NpmRegistry);
        cache.Set(CliIdentityType.Codex, "0.153.4", CliVersionSource.GitHubReleases);

        // 直接从 SQLite 读取持久化内容
        var csb = new SqliteConnectionStringBuilder(_connectionString);
        csb.Pooling = false;
        using var connection = new SqliteConnection(csb.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CliType, Version, Source FROM CliVersionEntries";
        var rows = new Dictionary<string, (string Version, string Source)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                rows[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
            }
        }

        Assert.True(rows.ContainsKey("ClaudeCode"));
        Assert.Equal("2.1.263", rows["ClaudeCode"].Version);
        Assert.True(rows.ContainsKey("Codex"));
        Assert.Equal("0.153.4", rows["Codex"].Version);
    }

    [Fact]
    public void Load_MissingTableReturnsEmptyCache()
    {
        // 数据库尚未建表时，CliVersionCache.Get/IsStale 应静默降级
        var freshPath = Path.Combine(Path.GetTempPath(), "loomx-tests", Guid.NewGuid().ToString("N") + ".db");
        var freshCs = new SqliteConnectionStringBuilder { DataSource = freshPath, Pooling = true }.ToString();
        try
        {
            var cache = new CliVersionCache(freshCs);
            Assert.Null(cache.Get(CliIdentityType.ClaudeCode));
            Assert.True(cache.IsStale(CliIdentityType.ClaudeCode));
        }
        finally
        {
            try { File.Delete(freshPath); } catch { }
        }
    }

    [Fact]
    public void Set_CreatesMissingDirectory()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), "loomx-tests", Guid.NewGuid().ToString("N"), "nested");
        var nestedPath = Path.Combine(nestedDir, "cli-versions.db");
        var nestedCs = new SqliteConnectionStringBuilder { DataSource = nestedPath, Pooling = true }.ToString();
        var cache = new CliVersionCache(nestedCs, now: () => DateTimeOffset.UtcNow);

        cache.Set(CliIdentityType.Grok, "1.0.6", CliVersionSource.Default);

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void MigrateLegacyFromJson_CopiesEntriesAndDeletesSource()
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), "loomx-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        var json = """
            {
              "entries": {
                "ClaudeCode": { "version": "1.2.3", "fetchedAt": "2026-08-01T00:00:00Z", "source": "NpmRegistry" },
                "Codex": { "version": "0.150.0", "fetchedAt": "2026-08-01T00:00:00Z", "source": "GitHubReleases" }
              }
            }
            """;
        File.WriteAllText(legacyPath, json);

        try
        {
            var cache = new CliVersionCache(_connectionString);
            cache.MigrateLegacyFromJson(legacyPath);

            var claude = cache.Get(CliIdentityType.ClaudeCode);
            Assert.NotNull(claude);
            Assert.Equal("1.2.3", claude!.Version);
            Assert.Equal(CliVersionSource.NpmRegistry, claude.Source);

            var codex = cache.Get(CliIdentityType.Codex);
            Assert.NotNull(codex);
            Assert.Equal("0.150.0", codex!.Version);

            // 迁移成功后删除旧文件
            Assert.False(File.Exists(legacyPath));
        }
        finally
        {
            try { File.Delete(legacyPath); } catch { }
        }
    }

    [Fact]
    public void MigrateLegacyFromJson_DoesNotOverwriteUserOverride()
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), "loomx-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        var json = """
            {
              "entries": {
                "Grok": { "version": "9.9.9", "fetchedAt": "2026-08-01T00:00:00Z", "source": "Default" }
              }
            }
            """;
        File.WriteAllText(legacyPath, json);

        var cache = new CliVersionCache(_connectionString);
        cache.SetUserOverride(CliIdentityType.Grok, "8.8.8");

        try
        {
            cache.MigrateLegacyFromJson(legacyPath);

            var info = cache.Get(CliIdentityType.Grok)!;
            Assert.Equal("8.8.8", info.Version);
            Assert.Equal(CliVersionSource.UserOverridden, info.Source);
        }
        finally
        {
            try { File.Delete(legacyPath); } catch { }
        }
    }

    [Fact]
    public void MigrateLegacyFromJson_InvalidJsonKeepsFile()
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), "loomx-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "not valid json {{{");

        try
        {
            var cache = new CliVersionCache(_connectionString);
            cache.MigrateLegacyFromJson(legacyPath);

            Assert.Null(cache.Get(CliIdentityType.ClaudeCode));
            // 无法解析的旧文件应保留原样，不覆盖
            Assert.True(File.Exists(legacyPath));
        }
        finally
        {
            try { File.Delete(legacyPath); } catch { }
        }
    }

    [Fact]
    public void Get_MissingLegacyFile_SkipsMigrationSilently()
    {
        var cache = new CliVersionCache(_connectionString);
        // 无旧文件时应保持空缓存且不抛异常
        Assert.Null(cache.Get(CliIdentityType.ClaudeCode));
        Assert.True(cache.IsStale(CliIdentityType.ClaudeCode));
    }
}
