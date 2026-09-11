namespace LoomX.Services;

/// <summary>
/// CLI 身份类型。三家官方 CLI 各对应一个枚举值。
/// </summary>
public enum CliIdentityType
{
    ClaudeCode,
    Codex,
    Grok
}

/// <summary>
/// 版本来源标记，用于 UI 上区分动态/缓存/默认/用户覆盖。
/// </summary>
public enum CliVersionSource
{
    NpmRegistry,
    GitHubReleases,
    Default,
    Cached,
    UserOverridden
}

/// <summary>
/// CLI 静态配置：显示名、UA 模板、静态头、剥除本家族的头键集合。
/// 头键集合与 LiveAgent CLI_IDENTITY_HEADER_FAMILIES 对齐（含 x-stainless-* 完整枚举）。
/// </summary>
public sealed record CliIdentityProfile(
    CliIdentityType Type,
    string DisplayName,
    string UaPrefix,
    string UaTemplate,
    IReadOnlyDictionary<string, string> StaticHeaders,
    IReadOnlyList<string> FamilyHeaderKeys);

/// <summary>
/// CLI 版本动态数据。
/// </summary>
public sealed record CliVersionInfo(
    CliIdentityType Type,
    string Version,
    DateTimeOffset? FetchedAt,
    CliVersionSource Source);

/// <summary>
/// 静态配置 + 家族剥除 + 合并 + 反推。所有方法都无副作用（不修改传入字典，返回新字典）。
/// </summary>
public static class CliIdentityService
{
    private static readonly CliIdentityProfile ClaudeCodeProfile = new(
        CliIdentityType.ClaudeCode,
        "Claude Code",
        "claude-cli/",
        "claude-cli/{version} (external, cli)",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic-version"] = "2023-06-01",
            ["x-stainless-lang"] = "js",
            ["x-stainless-os"] = "Linux",
            ["x-stainless-arch"] = "x64",
            ["x-stainless-runtime"] = "node",
            ["x-stainless-runtime-version"] = "22.14.0",
            ["x-stainless-package-version"] = "{version}",
            ["x-stainless-timeout"] = "600000",
            ["x-stainless-retries"] = "2",
        },
        new[]
        {
            "User-Agent",
            "anthropic-version",
            "x-stainless-lang",
            "x-stainless-package-version",
            "x-stainless-os",
            "x-stainless-arch",
            "x-stainless-runtime",
            "x-stainless-runtime-version",
            "x-stainless-timeout",
            "x-stainless-retries",
        });

    private static readonly CliIdentityProfile CodexProfile = new(
        CliIdentityType.Codex,
        "Codex CLI",
        "codex_cli_rs/",
        "codex_cli_rs/{version}",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["originator"] = "codex_cli_rs",
            ["version"] = "{version}",
        },
        new[]
        {
            "User-Agent",
            "originator",
            "version",
        });

    private static readonly CliIdentityProfile GrokProfile = new(
        CliIdentityType.Grok,
        "Grok CLI",
        "grok-cli/",
        "grok-cli/{version} (external, cli)",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-grok-client-identifier"] = "grok-cli",
            ["x-grok-client-version"] = "{version}",
            ["x-grok-client-mode"] = "cli",
            ["X-XAI-Token-Auth"] = "true",
            ["x-authenticateresponse"] = "true",
        },
        new[]
        {
            "User-Agent",
            "x-grok-client-identifier",
            "x-grok-client-version",
            "x-grok-client-mode",
            "X-XAI-Token-Auth",
            "x-authenticateresponse",
        });

    public static IReadOnlyDictionary<CliIdentityType, CliIdentityProfile> Profiles { get; } =
        new Dictionary<CliIdentityType, CliIdentityProfile>
        {
            [CliIdentityType.ClaudeCode] = ClaudeCodeProfile,
            [CliIdentityType.Codex] = CodexProfile,
            [CliIdentityType.Grok] = GrokProfile,
        };

    public static CliIdentityProfile GetProfile(CliIdentityType type) => Profiles[type];

    /// <summary>
    /// 三家 CLI 各家族完整头键集合的并集（用于「剥除所有已知家族键」）。
    /// 键比较使用 OrdinalIgnoreCase。
    /// </summary>
    public static IReadOnlyList<string> AllFamilyHeaderKeys { get; } =
        Profiles.Values.SelectMany(p => p.FamilyHeaderKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static bool IsFamilyHeaderKey(string key) =>
        key.StartsWith("x-stainless-", StringComparison.OrdinalIgnoreCase)
        || AllFamilyHeaderKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 给定 CLI 家族和版本号，构造该家族的完整头集合。
    /// 模板中的 {version} 占位符被替换为实际版本。
    /// </summary>
    public static Dictionary<string, string> BuildCliIdentityHeaders(CliIdentityType type, string version)
    {
        var profile = GetProfile(type);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = profile.UaTemplate.Replace("{version}", version, StringComparison.Ordinal),
        };
        foreach (var header in profile.StaticHeaders)
        {
            var value = header.Value.Contains("{version}", StringComparison.Ordinal)
                ? header.Value.Replace("{version}", version, StringComparison.Ordinal)
                : header.Value;
            result[header.Key] = value;
        }
        return result;
    }

    /// <summary>
    /// 应用 CLI 身份：剥除所有已知家族头，然后合并目标家族头。
    /// 非家族头保持不变。返回新字典，不修改输入。
    /// </summary>
    public static Dictionary<string, string> ApplyCliIdentity(
        IReadOnlyDictionary<string, string> headers,
        CliIdentityType targetType,
        string version)
    {
        var result = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        foreach (var key in result.Keys.Where(IsFamilyHeaderKey).ToArray())
            result.Remove(key);

        foreach (var (key, value) in BuildCliIdentityHeaders(targetType, version))
            result[key] = value;

        return result;
    }

    /// <summary>
    /// 从现有 header 反推当前应用的是哪家 CLI 身份。
    /// 匹配规则：User-Agent 前缀匹配 profile.UaPrefix。
    /// 未匹配返回 null。
    /// </summary>
    public static CliIdentityType? DetectCliIdentity(IReadOnlyDictionary<string, string> headers)
    {
        if (headers is null || !headers.TryGetValue("User-Agent", out var ua) || string.IsNullOrWhiteSpace(ua))
            return null;
        foreach (var profile in Profiles.Values)
        {
            if (ua.StartsWith(profile.UaPrefix, StringComparison.OrdinalIgnoreCase))
                return profile.Type;
        }
        if (ua.StartsWith("codex-cli/", StringComparison.OrdinalIgnoreCase))
            return CliIdentityType.Codex;
        return null;
    }

    /// <summary>
    /// 从现有 header 反推已应用的 CLI 版本。
    /// 匹配规则：从 UA 或 profile.StaticHeaders 中带 {version} 的头里解析。
    /// </summary>
    public static string? DetectCliVersion(IReadOnlyDictionary<string, string> headers, CliIdentityType type)
    {
        if (headers is null) return null;
        var profile = GetProfile(type);
        if (headers.TryGetValue("User-Agent", out var ua))
        {
            var prefix = ua.StartsWith(profile.UaPrefix, StringComparison.OrdinalIgnoreCase)
                ? profile.UaPrefix
                : type == CliIdentityType.Codex && ua.StartsWith("codex-cli/", StringComparison.OrdinalIgnoreCase)
                    ? "codex-cli/"
                    : null;
            if (prefix is null) return null;
            var rest = ua[prefix.Length..].Trim();
            // 形如 "2.1.263 (external, cli)" 或 "2.1.263"
            var separator = rest.IndexOfAny(new[] { ' ', '(' });
            if (separator > 0) return rest[..separator].Trim();
            return rest;
        }
        return null;
    }
}
