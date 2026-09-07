using System.Net;
using System.Text.Json;
using LoomX.Localization;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

/// <summary>
/// CLI 版本获取服务。三路来源：
/// - Claude：registry.npmjs.org/@anthropic-ai/claude-code/latest → JSON .version
/// - Codex：api.github.com/repos/openai/codex/releases/latest → JSON .name / .tag_name（去 rust- 前缀）
/// - Grok：无公开源，默认 1.0.6 + 用户可覆盖
/// 失败降级链：fetch → cache → default。所有异常都静默降级，日志记录失败原因。
/// </summary>
public sealed class CliVersionService
{
    public const string ClaudeDefaultVersion = "2.1.263";
    public const string CodexDefaultVersion = "0.153.4";
    public const string GrokDefaultVersion = "1.0.6";

    private const string ClaudeApiUrl = "https://registry.npmjs.org/@anthropic-ai/claude-code/latest";
    private const string CodexApiUrl = "https://api.github.com/repos/openai/codex/releases/latest";

    private readonly Func<CliVersionService, HttpClient> _clientFactory;
    private readonly CliVersionCache _cache;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILogger<CliVersionService> _logger;

    public CliVersionService(
        Func<CliVersionService, HttpClient>? clientFactory = null,
        CliVersionCache? cache = null,
        Func<DateTimeOffset>? now = null,
        ILogger<CliVersionService>? logger = null)
    {
        _clientFactory = clientFactory ?? DefaultClientFactory;
        _cache = cache ?? new CliVersionCache();
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CliVersionService>.Instance;
    }

    /// <summary>
    /// 获取指定 CLI 的版本。三级降级：
    /// 1. 若用户覆盖则直接返回（跳过网络）。
    /// 2. 若缓存新鲜则使用缓存。
    /// 3. 若过期或缺失则尝试 fetch；fetch 失败降级到缓存，缓存也缺失则使用默认。
    /// </summary>
    public async Task<CliVersionInfo> GetVersionAsync(CliIdentityType type, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (_cache.HasUserOverride(type))
        {
            return _cache.Get(type) ?? new CliVersionInfo(type, GetDefaultVersion(type), _now(), CliVersionSource.UserOverridden);
        }

        var cached = _cache.Get(type);
        if (cached is not null && !forceRefresh && !_cache.IsStale(type))
        {
            return cached with { Source = CliVersionSource.Cached };
        }

        try
        {
            var (version, source) = await FetchRemoteVersionAsync(type, cancellationToken);
            _cache.Set(type, version, source, fetchedAt: _now());
            return new CliVersionInfo(type, version, _now(), source);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogDebug(exception, "CLI 版本获取失败 type={Type}，使用缓存或默认降级。", type);
            if (cached is not null)
                return cached with { Source = CliVersionSource.Cached };
            return new CliVersionInfo(type, GetDefaultVersion(type), null, CliVersionSource.Default);
        }
    }

    /// <summary>
    /// 从 npm registry 拉取 Claude Code 最新版本。
    /// </summary>
    private async Task<(string Version, CliVersionSource Source)> FetchClaudeVersionAsync(CancellationToken cancellationToken)
    {
        using var client = _clientFactory(this);
        using var response = await client.GetAsync(ClaudeApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var version = document.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("npm registry 响应缺少 version 字段。");
        return (version.Trim(), CliVersionSource.NpmRegistry);
    }

    /// <summary>
    /// 从 GitHub releases API 拉取 Codex 最新版本。
    /// </summary>
    private async Task<(string Version, CliVersionSource Source)> FetchCodexVersionAsync(CancellationToken cancellationToken)
    {
        using var client = _clientFactory(this);
        using var response = await client.GetAsync(CodexApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var rawName = root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(rawName))
            rawName = root.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String
                ? tag.GetString()
                : null;
        if (string.IsNullOrWhiteSpace(rawName))
            throw new InvalidOperationException("GitHub releases 响应缺少 name/tag_name 字段。");
        var version = ParseCodexVersion(rawName);
        return (version, CliVersionSource.GitHubReleases);
    }

    /// <summary>
    /// 解析 Codex 版本：
    /// - 优先从 name 取末尾的 semver（如 "codex 0.153.4" → "0.153.4"）
    /// - name 缺失时用 tag_name 去 "rust-" 或 "v" 前缀
    /// </summary>
    internal static string ParseCodexVersion(string raw)
    {
        var value = raw.Trim();
        // 尝试从任意位置提取 semver
        var match = System.Text.RegularExpressions.Regex.Match(value, @"(\d+\.\d+\.\d+)");
        if (match.Success) return match.Groups[1].Value;
        // 否则按前缀去除
        return value
            .TrimStart('v', 'V')
            .TrimStart('r', 'R');
    }

    private async Task<(string Version, CliVersionSource Source)> FetchRemoteVersionAsync(CliIdentityType type, CancellationToken cancellationToken)
    {
        return type switch
        {
            CliIdentityType.ClaudeCode => await FetchClaudeVersionAsync(cancellationToken),
            CliIdentityType.Codex => await FetchCodexVersionAsync(cancellationToken),
            CliIdentityType.Grok => (GetDefaultVersion(CliIdentityType.Grok), CliVersionSource.Default),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    public static string GetDefaultVersion(CliIdentityType type) => type switch
    {
        CliIdentityType.ClaudeCode => ClaudeDefaultVersion,
        CliIdentityType.Codex => CodexDefaultVersion,
        CliIdentityType.Grok => GrokDefaultVersion,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static HttpClient DefaultClientFactory(CliVersionService _)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("LoomX", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("cli-identity-fetch", "1.0"));
        return client;
    }
}
