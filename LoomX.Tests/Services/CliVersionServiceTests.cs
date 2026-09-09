using System.Net;
using System.Net.Http;
using System.Text;
using LoomX.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class CliVersionServiceTests : IDisposable
{
    private readonly string _cachePath;
    private readonly string _connectionString;

    public CliVersionServiceTests()
    {
        _cachePath = Path.Combine(Path.GetTempPath(), "loomx-tests", Guid.NewGuid().ToString("N") + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _cachePath,
            Pooling = true,
        }.ToString();
    }

    public void Dispose()
    {
        try { File.Delete(_cachePath); } catch { }
    }

    [Fact]
    public async Task GetVersionAsync_ClaudeCode_ReturnsVersionFromNpmJson()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"name":"@anthropic-ai/claude-code","version":"2.1.263","description":"stub"}
                """, Encoding.UTF8, "application/json")
        });

        var service = new CliVersionService(
            _ => new HttpClient(handler),
            new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow));

        var info = await service.GetVersionAsync(CliIdentityType.ClaudeCode);

        Assert.Equal("2.1.263", info.Version);
        Assert.Equal(CliVersionSource.NpmRegistry, info.Source);
        Assert.Equal(CliIdentityType.ClaudeCode, info.Type);
    }

    [Fact]
    public async Task GetVersionAsync_Codex_ReturnsVersionFromGitHubJsonName()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"tag_name":"rust-v0.153.4","name":"codex 0.153.4","draft":false,"prerelease":false}
                """, Encoding.UTF8, "application/json")
        });

        var service = new CliVersionService(
            _ => new HttpClient(handler),
            new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow));

        var info = await service.GetVersionAsync(CliIdentityType.Codex);

        Assert.Equal("0.153.4", info.Version);
        Assert.Equal(CliVersionSource.GitHubReleases, info.Source);
    }

    [Fact]
    public async Task GetVersionAsync_Codex_FallsBackToTagNameWhenNameMissing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"tag_name":"rust-v0.153.4","name":null}
                """, Encoding.UTF8, "application/json")
        });

        var service = new CliVersionService(
            _ => new HttpClient(handler),
            new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow));

        var info = await service.GetVersionAsync(CliIdentityType.Codex);

        Assert.Equal("0.153.4", info.Version);
    }

    [Fact]
    public async Task GetVersionAsync_Codex_VPrefixStripped()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"tag_name":"v0.153.4","name":"0.153.4"}
                """, Encoding.UTF8, "application/json")
        });

        var service = new CliVersionService(
            _ => new HttpClient(handler),
            new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow));

        var info = await service.GetVersionAsync(CliIdentityType.Codex);

        Assert.Equal("0.153.4", info.Version);
    }

    [Fact]
    public async Task GetVersionAsync_Grok_ReturnsDefault()
    {
        // Grok 无公开版本源，不触发 HTTP 请求；返回默认版本。
        var service = new CliVersionService(
            _ => new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Grok should not fetch"))),
            new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow));

        var info = await service.GetVersionAsync(CliIdentityType.Grok);

        Assert.Equal(CliVersionService.GrokDefaultVersion, info.Version);
        Assert.Equal(CliVersionSource.Default, info.Source);
    }

    [Fact]
    public async Task GetVersionAsync_FetchFailure_ReturnsCachedValue()
    {
        var cacheCs = _connectionString;
        var now = DateTimeOffset.UtcNow;
        var cache = new CliVersionCache(cacheCs, now: () => now);
        cache.Set(CliIdentityType.ClaudeCode, "2.1.260", CliVersionSource.NpmRegistry);

        var service = new CliVersionService(
            _ => new HttpClient(new StubHandler(_ => throw new HttpRequestException("network down"))),
            cache);

        var info = await service.GetVersionAsync(CliIdentityType.ClaudeCode);

        Assert.Equal("2.1.260", info.Version);
        Assert.Equal(CliVersionSource.Cached, info.Source);
    }

    [Fact]
    public async Task GetVersionAsync_FetchFailure_NoCache_ReturnsDefault()
    {
        var service = new CliVersionService(
            _ => new HttpClient(new StubHandler(_ => throw new HttpRequestException("network down"))),
            new CliVersionCache(_connectionString, now: () => DateTimeOffset.UtcNow));

        var info = await service.GetVersionAsync(CliIdentityType.Codex);

        Assert.Equal(CliVersionService.CodexDefaultVersion, info.Version);
        Assert.Equal(CliVersionSource.Default, info.Source);
    }

    [Fact]
    public async Task GetVersionAsync_UserOverride_SkipsNetworkAndReturnsOverride()
    {
        var cacheCs = _connectionString;
        var now = DateTimeOffset.UtcNow;
        var cache = new CliVersionCache(cacheCs, now: () => now);
        cache.SetUserOverride(CliIdentityType.Grok, "9.9.9");

        var service = new CliVersionService(
            _ => new HttpClient(new StubHandler(_ => throw new InvalidOperationException("should not fetch"))),
            cache);

        var info = await service.GetVersionAsync(CliIdentityType.Grok);

        Assert.Equal("9.9.9", info.Version);
        Assert.Equal(CliVersionSource.UserOverridden, info.Source);
    }

    [Fact]
    public async Task GetVersionAsync_FreshCache_SkipsNetwork()
    {
        var cacheCs = _connectionString;
        var now = new DateTimeOffset(2026, 9, 7, 2, 0, 0, TimeSpan.Zero);
        var cache = new CliVersionCache(cacheCs, now: () => now);
        cache.Set(CliIdentityType.ClaudeCode, "2.1.260", CliVersionSource.NpmRegistry);

        var service = new CliVersionService(
            _ => new HttpClient(new StubHandler(_ => throw new InvalidOperationException("should not fetch"))),
            cache);

        var info = await service.GetVersionAsync(CliIdentityType.ClaudeCode);

        Assert.Equal("2.1.260", info.Version);
        Assert.Equal(CliVersionSource.Cached, info.Source);
    }

    [Fact]
    public async Task GetVersionAsync_ExpiredCache_TriggersRefreshAndPersists()
    {
        var cacheCs = _connectionString;
        var startTime = new DateTimeOffset(2026, 9, 7, 2, 0, 0, TimeSpan.Zero);
        var cache = new CliVersionCache(cacheCs, ttl: TimeSpan.FromHours(24), now: () => startTime);
        cache.Set(CliIdentityType.ClaudeCode, "2.1.260", CliVersionSource.NpmRegistry);

        // 时间推进超过 TTL，缓存过期
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"version":"2.1.263"}
                """, Encoding.UTF8, "application/json")
        });
        var laterCache = new CliVersionCache(cacheCs, ttl: TimeSpan.FromHours(24), now: () => startTime.AddHours(25));
        var service = new CliVersionService(_ => new HttpClient(handler), laterCache);

        var info = await service.GetVersionAsync(CliIdentityType.ClaudeCode);

        Assert.Equal("2.1.263", info.Version);
        Assert.Equal(CliVersionSource.NpmRegistry, info.Source);
    }

    [Theory]
    [InlineData("0.153.4", "0.153.4")]
    [InlineData("v0.153.4", "0.153.4")]
    [InlineData("V0.153.4", "0.153.4")]
    [InlineData("rust-v0.153.4", "0.153.4")]
    [InlineData("codex 0.153.4", "0.153.4")]
    [InlineData("release 1.2.3-beta.4", "1.2.3")]
    public void ParseCodexVersion_ExtractsSemver(string raw, string expected)
    {
        Assert.Equal(expected, CliVersionService.ParseCodexVersion(raw));
    }

    [Fact]
    public static void GetDefaultVersion_ReturnsExpected()
    {
        Assert.Equal("2.1.88", CliVersionService.GetDefaultVersion(CliIdentityType.ClaudeCode));
        Assert.Equal("0.151.0", CliVersionService.GetDefaultVersion(CliIdentityType.Codex));
        Assert.Equal("1.0.6", CliVersionService.GetDefaultVersion(CliIdentityType.Grok));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
