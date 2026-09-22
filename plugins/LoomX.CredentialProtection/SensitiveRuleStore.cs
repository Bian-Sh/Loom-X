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
/// Plugin-owned 敏感规则存储：规则持久化在插件自有 JSON 文件（插件数据目录下 rules.json），
/// 支持启用/禁用/增删，不写宿主核心配置。文件缺失时以内置基线初始化。
/// </summary>
public sealed class SensitiveRuleStore
{
    private const string RulesFileName = "rules.json";

    private static readonly JsonSerializerOptions StoreJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly string rulesFilePath;
    private readonly object gate = new();

    public string DataDirectory { get; }
    private List<SensitiveRule> rules;

    public SensitiveRuleStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(DataDirectory);
        rulesFilePath = Path.Combine(DataDirectory, RulesFileName);
        rules = Load();
    }

    /// <summary>规则版本号：每次变更递增，检测/脱敏引擎按版本号重建缓存。</summary>
    public int Version { get; private set; }

    public IReadOnlyList<SensitiveRule> Rules
    {
        get { lock (gate) return rules.ToArray(); }
    }

    /// <summary>新增自定义规则；id 冲突时返回 false。</summary>
    public bool AddRule(SensitiveRuleKind kind, string value, out SensitiveRule? rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        lock (gate)
        {
            var id = $"custom.{kind.ToString().ToLowerInvariant()}.{rules.Count(item => !item.Builtin) + 1}";
            if (rules.Any(item => item.Kind == kind && item.Value == value))
            {
                rule = null;
                return false;
            }

            rule = new SensitiveRule(id, kind, value, Enabled: true, Builtin: false);
            rules.Add(rule);
            Save();
            return true;
        }
    }

    /// <summary>删除规则（内置与自定义均可删除；基线只在文件缺失时重建）。</summary>
    public bool RemoveRule(string ruleId)
    {
        lock (gate)
        {
            var removed = rules.RemoveAll(item => item.Id == ruleId) > 0;
            if (removed) Save();
            return removed;
        }
    }

    public bool SetRuleEnabled(string ruleId, bool enabled)
    {
        lock (gate)
        {
            var index = rules.FindIndex(item => item.Id == ruleId);
            if (index < 0) return false;
            rules[index] = rules[index] with { Enabled = enabled };
            Save();
            return true;
        }
    }

    private List<SensitiveRule> Load()
    {
        if (!File.Exists(rulesFilePath)) return BaselineRules();
        try
        {
            var dto = JsonSerializer.Deserialize<RulesFileDto>(File.ReadAllText(rulesFilePath), StoreJsonOptions);
            if (dto?.Rules is { Count: > 0 } stored)
            {
                return stored
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Value))
                    .Select(item => new SensitiveRule(item.Id!, item.Kind, item.Value!, item.Enabled, item.Builtin))
                    .ToList();
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
