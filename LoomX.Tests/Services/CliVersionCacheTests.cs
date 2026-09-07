using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class CliVersionCacheTests : IDisposable
{
    private readonly string _tempPath;

    public CliVersionCacheTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "loomx-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(_tempPath)!);
    }

    public void Dispose()
    {
        try { File.Delete(_tempPath); } catch { /* 清理失败忽略 */ }
    }

    [Fact]
    public void IsStale_FreshCache_ReturnsFalse()
    {
        var cache = new CliVersionCache(_tempPath, now: () => DateTimeOffset.UtcNow);
        cache.Set(CliIdentityType.ClaudeCode, "2.1.263", CliVersionSource.NpmRegistry);

        Assert.False(cache.IsStale(CliIdentityType.ClaudeCode));
    }

    [Fact]
    public void IsStale_ExpiredCache_ReturnsTrue()
    {
        var baseTime = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var cache = new CliVersionCache(_tempPath, ttl: TimeSpan.FromHours(24), now: () => baseTime);
        cache.Set(CliIdentityType.ClaudeCode, "2.1.263", CliVersionSource.NpmRegistry);

        // 时间推进到超过 TTL
        var laterCache = new CliVersionCache(_tempPath, ttl: TimeSpan.FromHours(24), now: () => baseTime.AddHours(25));
        Assert.True(laterCache.IsStale(CliIdentityType.ClaudeCode));
    }

    [Fact]
    public void IsStale_MissingCache_ReturnsTrue()
    {
        var cache = new CliVersionCache(_tempPath);
        Assert.True(cache.IsStale(CliIdentityType.Grok));
    }

    [Fact]
    public void Get_ReturnsStoredVersion()
    {
        var cache = new CliVersionCache(_tempPath, now: () => DateTimeOffset.UtcNow);
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
        var cache = new CliVersionCache(_tempPath, now: () => DateTimeOffset.UtcNow);
        cache.SetUserOverride(CliIdentityType.Grok, "9.9.9");

        Assert.True(cache.HasUserOverride(CliIdentityType.Grok));
        var info = cache.Get(CliIdentityType.Grok)!;
        Assert.Equal("9.9.9", info.Version);
        Assert.Equal(CliVersionSource.UserOverridden, info.Source);
    }

    [Fact]
    public void HasUserOverride_FalseWhenNotSet()
    {
        var cache = new CliVersionCache(_tempPath);
        Assert.False(cache.HasUserOverride(CliIdentityType.Grok));
    }

    [Fact]
    public void IsStale_UserOverrideReturnsFalse()
    {
        var cache = new CliVersionCache(_tempPath, now: () => DateTimeOffset.UtcNow);
        cache.SetUserOverride(CliIdentityType.Grok, "9.9.9");

        // 用户覆盖不受 TTL 影响
        var laterCache = new CliVersionCache(_tempPath, ttl: TimeSpan.FromHours(24), now: () => DateTimeOffset.UtcNow.AddHours(36));
        Assert.False(laterCache.IsStale(CliIdentityType.Grok));
    }

    [Fact]
    public void SetUserOverride_OverwritesPreviousDefault()
    {
        var cache = new CliVersionCache(_tempPath, now: () => DateTimeOffset.UtcNow);
        cache.Set(CliIdentityType.Grok, "1.0.6", CliVersionSource.Default);
        Assert.False(cache.HasUserOverride(CliIdentityType.Grok));

        cache.SetUserOverride(CliIdentityType.Grok, "2.0.0");

        Assert.True(cache.HasUserOverride(CliIdentityType.Grok));
        Assert.Equal("2.0.0", cache.Get(CliIdentityType.Grok)!.Version);
    }

    [Fact]
    public void ClearUserOverride_RemovesOverride()
    {
        var cache = new CliVersionCache(_tempPath, now: () => DateTimeOffset.UtcNow);
        cache.SetUserOverride(CliIdentityType.Grok, "2.0.0");
        Assert.True(cache.HasUserOverride(CliIdentityType.Grok));

        cache.ClearUserOverride(CliIdentityType.Grok);

        Assert.False(cache.HasUserOverride(CliIdentityType.Grok));
    }

    [Fact]
    public void Set_PersistsToDisk()
    {
        var cache = new CliVersionCache(_tempPath, now: () => DateTimeOffset.UtcNow);
        cache.Set(CliIdentityType.ClaudeCode, "2.1.263", CliVersionSource.NpmRegistry);
        cache.Set(CliIdentityType.Codex, "0.153.4", CliVersionSource.GitHubReleases);

        Assert.True(File.Exists(_tempPath));
        var raw = File.ReadAllText(_tempPath);
        Assert.Contains("2.1.263", raw, StringComparison.Ordinal);
        Assert.Contains("0.153.4", raw, StringComparison.Ordinal);
        // 两家 CLI 枚举值应作为 JSON 字典键出现（默认使用枚举名 PascalCase）
        Assert.Contains("ClaudeCode", raw, StringComparison.Ordinal);
        Assert.Contains("Codex", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_CorruptedFileReturnsEmptyCache()
    {
        File.WriteAllText(_tempPath, "not valid json {{{");

        var cache = new CliVersionCache(_tempPath);
        Assert.Null(cache.Get(CliIdentityType.ClaudeCode));
        Assert.True(cache.IsStale(CliIdentityType.ClaudeCode));
    }

    [Fact]
    public void Set_CreatesMissingDirectory()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), "loomx-tests", Guid.NewGuid().ToString("N"), "nested");
        var nestedPath = Path.Combine(nestedDir, "cli-versions.json");
        var cache = new CliVersionCache(nestedPath, now: () => DateTimeOffset.UtcNow);

        cache.Set(CliIdentityType.Grok, "1.0.6", CliVersionSource.Default);

        Assert.True(File.Exists(nestedPath));
        try { Directory.Delete(Path.GetTempPath(), recursive: false); } catch { }
    }
}
