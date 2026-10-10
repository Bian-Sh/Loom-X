using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoomX.CredentialProtection;

/// <summary>敏感规则类别：按名称（字段/路径段）或按值形态正则。</summary>
public enum SensitiveRuleKind
{
    Name,
    Pattern,
}

/// <summary>一条敏感规则。内置规则作为基线，自定义规则由用户增删。</summary>
public sealed record SensitiveRule(string Id, SensitiveRuleKind Kind, string Value, bool Enabled, bool Builtin);

/// <summary>
/// 内置形态规则：只可开关、不可删除。EnabledByDefault 与基线口径一致，
/// 公网 IP、内网 IP、IPv6、中国香港身份证、统一社会信用代码、MAC 默认关闭以避免过度脱敏。
/// </summary>
public sealed record CredentialBuiltinRule(string Key, string Pattern, bool EnabledByDefault);

/// <summary>内置形态规则基线表。</summary>
internal static class CredentialBuiltinRules
{
    /// <summary>
    /// 顺序即脱敏顺序：先窄后宽，避免短类别抢先吞掉长类别（身份证先于银行卡、私有 IP 先于公网 IP）。
    /// </summary>
    public static IReadOnlyList<CredentialBuiltinRule> Defaults =>
    [
        new("PRIVATE_KEY", @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", true),
        new("CONNSTR", @"(?<![A-Za-z0-9])(?:mongodb(?:\+srv)?|postgres(?:ql)?|mysql|redis|rediss|amqps?|mssql|sqlserver)://[^\s""'<>]+", true),
        new("JWT", @"(?<![A-Za-z0-9])eyJ[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}(?![A-Za-z0-9])", true),
        new("IDCARD", @"(?<!\d)\d{17}[\dXx](?!\d)", true),
        new("HKID", @"(?<![A-Z0-9])[A-Z]{1,2}\d{6}\([0-9A]\)", false),
        new("EMAIL", @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", true),
        new("PHONE", @"(?<!\d)1[3-9]\d{9}(?!\d)", true),
        new("LANDLINE", @"(?<![A-Za-z0-9])0\d{2,3}-?\d{7,8}(?![A-Za-z0-9])", true),
        new("PLATE", @"(?<![A-Z0-9])[京津沪渝冀豫云辽黑湘皖鲁新苏浙赣鄂桂甘晋蒙陕吉闽贵粤青藏川宁琼][A-Z][A-Z0-9]{5}(?![A-Z0-9])", true),
        new("IP_PRIVATE", @"(?<!\d)(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})(?!\d)", true),
        new("IP_INTERNAL", @"(?<![\w.])(?:localhost|127\.0\.0\.1|0\.0\.0\.0|::1)(?![\w.])", false),
        new("IPV6_PRIVATE", @"(?<![\w:])(?:fc00|fd[0-9a-f]{2}|fe80)(?::[0-9a-fA-F]{0,4}){2,7}", false),
        new("IP_PUBLIC", @"(?<!\d)(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?!\d)", false),
        new("CARD", @"(?<!\d)\d{16,19}(?!\d)", true),
        new("IBAN", @"(?<![A-Z0-9])[A-Z]{2}\d{2}[A-Z0-9]{11,30}(?![A-Z0-9])", true),
        new("USCC", @"(?<![A-Z0-9])[0-9A-HJ-NPQRTUWXY]{2}\d{6}[0-9A-HJ-NPQRTUWXY]{10}(?![A-Z0-9])", false),
        new("MAC", @"(?<![A-Fa-f0-9:])(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}(?![A-Fa-f0-9:])", false),
        new("API_KEY", @"(?<![A-Za-z0-9])(?:api[_-]?key|x-api-key)[""'\s:=]+[A-Za-z0-9._-]{8,}", true),
        new("ACCESS_KEY", @"(?<![\w])(?:AKIA|ASIA|LTAI|AKID)[A-Z0-9]{8,24}(?![\w])", true),
        new("TOKEN", @"(?<![A-Za-z0-9])(?:access[_-]?token|token)[""'\s:=]+[A-Za-z0-9._-]{8,}", true),
        new("SECRET", @"(?<![A-Za-z0-9])(?:client[_-]?secret|app[_-]?secret|secret)[""'\s:=]+[A-Za-z0-9._-]{8,}", true),
    ];

    /// <summary>凭据类标签：这类命中值绝不落到规则文件或统计里。</summary>
    public static readonly HashSet<string> CredentialKeys =
    [
        "API_KEY", "TOKEN", "SECRET", "ACCESS_KEY", "JWT", "CONNSTR", "PRIVATE_KEY",
    ];
}

/// <summary>
/// Plugin-owned 敏感规则存储：规则持久化在插件自有 JSON 文件（插件数据目录下 rules.json），
/// 支持启用/禁用/增删，不写宿主核心配置。文件缺失时以内置基线初始化。
/// </summary>
public sealed class SensitiveRuleStore
{
    private const string RulesFileName = "rules.json";
    private const int MaxValueLength = 512;
    private const int MaxPrefixLength = 32;
    private const int MaxRules = 200;
    private const int MaxPrefixes = 64;

    private static readonly JsonSerializerOptions StoreJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly string rulesFilePath;
    private readonly object gate = new();
    private int sequence;

    public string DataDirectory { get; }
    private List<SensitiveRule> rules;
    private Dictionary<string, bool> builtinEnabled;
    private List<string> secretPrefixes;

    public SensitiveRuleStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(DataDirectory);
        rulesFilePath = Path.Combine(DataDirectory, RulesFileName);
        builtinEnabled = CredentialBuiltinRules.Defaults.ToDictionary(
            item => item.Key, item => item.EnabledByDefault, StringComparer.Ordinal);
        secretPrefixes = [];
        rules = Load();
    }

    /// <summary>规则变更通知：插件据此让宿主刷新声明式 UI 快照。</summary>
    public event EventHandler? Changed;

    /// <summary>规则版本号：每次变更递增，检测/脱敏引擎按版本号重建缓存。</summary>
    public int Version { get; private set; }

    public IReadOnlyList<SensitiveRule> Rules
    {
        get { lock (gate) return rules.ToArray(); }
    }

    /// <summary>内置形态规则的启用态（key 为规则标识）。</summary>
    public IReadOnlyDictionary<string, bool> BuiltinRules
    {
        get { lock (gate) return new Dictionary<string, bool>(builtinEnabled, StringComparer.Ordinal); }
    }

    /// <summary>自定义密钥前缀：命中此前缀的值一律视为敏感。</summary>
    public IReadOnlyList<string> SecretPrefixes
    {
        get { lock (gate) return secretPrefixes.ToArray(); }
    }

    /// <summary>供引擎取用的启用态内置形态规则（按基线顺序）。</summary>
    public IReadOnlyList<CredentialBuiltinRule> EnabledBuiltinRules()
    {
        var enabled = BuiltinRules;
        return CredentialBuiltinRules.Defaults
            .Where(item => enabled.TryGetValue(item.Key, out var on) ? on : item.EnabledByDefault)
            .ToArray();
    }

    public IReadOnlyList<SensitiveRule> EnabledRules() => Rules.Where(item => item.Enabled).ToArray();

    /// <summary>新增自定义规则；校验失败或重复时返回 false 并给出可展示的原因。</summary>
    public bool AddRule(
        SensitiveRuleKind kind,
        string value,
        out SensitiveRule? rule,
        out string? error)
    {
        rule = null;
        if (!TryNormalize(value, kind, out var normalized, out error))
            return false;

        lock (gate)
        {
            if (rules.Count >= MaxRules)
            {
                error = "TOO_MANY_RULES";
                return false;
            }
            if (rules.Any(item => item.Kind == kind && item.Value == normalized))
            {
                error = "DUPLICATE_RULE";
                return false;
            }

            rule = new SensitiveRule(
                $"custom.{kind.ToString().ToLowerInvariant()}.{++sequence}",
                kind,
                normalized,
                Enabled: true,
                Builtin: false);
            rules.Add(rule);
            Save();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>更新自定义规则的值；未找到或校验失败时返回 false。</summary>
    public bool UpdateRule(string ruleId, string value, out string? error)
    {
        lock (gate)
        {
            var index = rules.FindIndex(item => item.Id == ruleId);
            if (index < 0)
            {
                error = "RULE_NOT_FOUND";
                return false;
            }

            var existing = rules[index];
            if (!TryNormalize(value, existing.Kind, out var normalized, out error))
                return false;

            if (rules.Any(item => item.Id != ruleId
                && item.Kind == existing.Kind
                && item.Value == normalized))
            {
                error = "DUPLICATE_RULE";
                return false;
            }

            rules[index] = existing with { Value = normalized };
            Save();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>删除规则（内置与自定义均可删除；基线只在文件缺失时重建）。</summary>
    public bool RemoveRule(string ruleId)
    {
        lock (gate)
        {
            var removed = rules.RemoveAll(item => item.Id == ruleId) > 0;
            if (removed) Save();
            else return false;
        }

        RaiseChanged();
        return true;
    }

    public bool SetRuleEnabled(string ruleId, bool enabled)
    {
        lock (gate)
        {
            var index = rules.FindIndex(item => item.Id == ruleId);
            if (index < 0) return false;
            if (rules[index].Enabled == enabled) return false;
            rules[index] = rules[index] with { Enabled = enabled };
            Save();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>开关内置形态规则；key 不存在时返回 false。</summary>
    public bool SetBuiltinEnabled(string key, bool enabled)
    {
        lock (gate)
        {
            if (!builtinEnabled.ContainsKey(key)) return false;
            if (builtinEnabled[key] == enabled) return false;
            builtinEnabled[key] = enabled;
            Save();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>批量开关全部内置形态规则；状态已全部一致时返回 false（无需落盘）。</summary>
    public bool SetAllBuiltinEnabled(bool enabled)
    {
        lock (gate)
        {
            var changed = false;
            foreach (var rule in CredentialBuiltinRules.Defaults)
            {
                if (builtinEnabled[rule.Key] == enabled) continue;
                builtinEnabled[rule.Key] = enabled;
                changed = true;
            }
            if (!changed) return false;
            Save();
        }

        RaiseChanged();
        return true;
    }

    public bool AddPrefix(string prefix, out string? error)
    {
        error = null;
        var normalized = (prefix ?? string.Empty).Trim();
        if (normalized.Length is 0 or > MaxPrefixLength
            || normalized.Any(char.IsWhiteSpace)
            || normalized.Any(char.IsControl))
        {
            error = "INVALID_PREFIX";
            return false;
        }

        lock (gate)
        {
            if (secretPrefixes.Count >= MaxPrefixes)
            {
                error = "TOO_MANY_PREFIXES";
                return false;
            }
            if (secretPrefixes.Contains(normalized, StringComparer.Ordinal))
            {
                error = "DUPLICATE_PREFIX";
                return false;
            }
            secretPrefixes.Add(normalized);
            Save();
        }

        RaiseChanged();
        return true;
    }

    public bool RemovePrefix(string prefix)
    {
        lock (gate)
        {
            if (secretPrefixes.RemoveAll(item => string.Equals(item, prefix, StringComparison.Ordinal)) == 0)
                return false;
            Save();
        }

        RaiseChanged();
        return true;
    }

    private static bool TryNormalize(
        string? value,
        SensitiveRuleKind kind,
        out string normalized,
        out string? error)
    {
        normalized = (value ?? string.Empty).Trim();
        error = null;

        if (normalized.Length == 0 || normalized.Length > MaxValueLength)
        {
            error = "INVALID_VALUE";
            return false;
        }
        if (kind == SensitiveRuleKind.Pattern)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(
                    normalized,
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException)
            {
                error = "INVALID_PATTERN";
                return false;
            }
        }
        return true;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private List<SensitiveRule> Load()
    {
        if (!File.Exists(rulesFilePath)) return BaselineRules();
        try
        {
            var dto = JsonSerializer.Deserialize<RulesFileDto>(File.ReadAllText(rulesFilePath), StoreJsonOptions);
            if (dto?.Rules is { Count: > 0 } stored)
            {
                foreach (var (key, enabled) in dto.BuiltinRules ?? [])
                {
                    if (builtinEnabled.ContainsKey(key)) builtinEnabled[key] = enabled;
                }

                secretPrefixes = (dto.SecretPrefixes ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .ToList();

                var loaded = stored
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Value))
                    .Select(item => new SensitiveRule(item.Id!, item.Kind, item.Value!, item.Enabled, item.Builtin))
                    .ToList();

                sequence = loaded
                    .Where(item => item.Id.StartsWith("custom.", StringComparison.Ordinal))
                    .Select(item => item.Id.Split('.').Length == 3
                        && int.TryParse(item.Id.Split('.')[2], out var number) ? number : 0)
                    .DefaultIfEmpty(0)
                    .Max();

                return loaded;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // 规则文件损坏：回退基线，不阻断插件加载。
        }

        return BaselineRules();
    }

    private void Save()
    {
        var dto = new RulesFileDto
        {
            Rules = rules.Select(item => new RuleDto
            {
                Id = item.Id,
                Kind = item.Kind,
                Value = item.Value,
                Enabled = item.Enabled,
                Builtin = item.Builtin,
            }).ToList(),
            BuiltinRules = new Dictionary<string, bool>(builtinEnabled, StringComparer.Ordinal),
            SecretPrefixes = [.. secretPrefixes],
        };
        var tempPath = rulesFilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(dto, StoreJsonOptions));
        File.Move(tempPath, rulesFilePath, overwrite: true);
        Version++;
    }

    /// <summary>
    /// 内置基线：语义迁移自旧助手保护基线（敏感名称集 + 值形态正则）。
    /// </summary>
    internal static List<SensitiveRule> BaselineRules() =>
    [
        // 敏感名称（字段/路径段）
        new("name.key", SensitiveRuleKind.Name, "key", true, true),
        new("name.api-key", SensitiveRuleKind.Name, "api_key", true, true),
        new("name.token", SensitiveRuleKind.Name, "token", true, true),
        new("name.password", SensitiveRuleKind.Name, "password", true, true),
        new("name.secret", SensitiveRuleKind.Name, "secret", true, true),
        new("name.authorization", SensitiveRuleKind.Name, "authorization", true, true),
        new("name.credential", SensitiveRuleKind.Name, "credential", true, true),
        new("name.headers", SensitiveRuleKind.Name, "headers", true, true),
        new("name.custom-headers", SensitiveRuleKind.Name, "custom_headers", true, true),
        new("name.http-headers", SensitiveRuleKind.Name, "http_headers", true, true),
        // 凭据值形态基线，并覆盖 Bearer 认证头。
        new("pattern.openai-sk", SensitiveRuleKind.Pattern,
            @"(?<![a-z0-9])sk-(?:proj-)?[a-z0-9_-]{12,}(?![a-z0-9])", true, true),
        new("pattern.github-token", SensitiveRuleKind.Pattern,
            @"(?<![a-z0-9])gh[pousr]_[a-z0-9]{20,}(?![a-z0-9])", true, true),
        new("pattern.github-pat", SensitiveRuleKind.Pattern,
            @"(?<![a-z0-9])github_pat_[a-z0-9_]{20,}(?![a-z0-9])", true, true),
        new("pattern.slack-xox", SensitiveRuleKind.Pattern,
            @"(?<![a-z0-9])xox[baprs]-[a-z0-9-]{10,}(?![a-z0-9])", true, true),
        new("pattern.aws-akid", SensitiveRuleKind.Pattern,
            @"(?<![a-z0-9])AKIA[0-9A-Z]{16}(?![a-z0-9])", true, true),
        new("pattern.google-api", SensitiveRuleKind.Pattern,
            @"(?<![a-z0-9])AIza[0-9A-Za-z_-]{20,}(?![a-z0-9])", true, true),
        new("pattern.jwt", SensitiveRuleKind.Pattern,
            @"(?<![a-z0-9])eyJ[a-z0-9_-]{6,}\.[a-z0-9_-]{6,}\.[a-z0-9_-]{6,}(?![a-z0-9])", true, true),
        new("pattern.bearer", SensitiveRuleKind.Pattern,
            @"Bearer\s+[A-Za-z0-9._~+/=-]{16,}", true, true),
    ];

    private sealed class RulesFileDto
    {
        public int Version { get; set; } = 1;
        public List<RuleDto>? Rules { get; set; }
        public Dictionary<string, bool>? BuiltinRules { get; set; }
        public List<string>? SecretPrefixes { get; set; }
    }

    private sealed class RuleDto
    {
        public string? Id { get; set; }
        public SensitiveRuleKind Kind { get; set; }
        public string? Value { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Builtin { get; set; }
    }
}
