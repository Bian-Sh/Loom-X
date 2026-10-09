using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LoomX.Maskit;

/// <summary>
/// Plugin-owned 规则存储：脱敏规则、内置形态开关与密钥前缀持久化在插件自有 rules.json，
/// 不写宿主核心配置。文件缺失时以内建基线初始化；规则值可含敏感词但绝不写回命中原文。
/// </summary>
public sealed class MaskitRuleStore
{
    private const string RulesFileName = "rules.json";
    private const int MaxCategoryLength = 64;
    private const int MaxWordLength = 256;
    private const int MaxPatternLength = 512;
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
    private List<MaskitRule> rules;
    private Dictionary<string, bool> builtinEnabled;
    private List<string> secretPrefixes;
    private int sequence;

    public MaskitRuleStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(DataDirectory);
        rulesFilePath = Path.Combine(DataDirectory, RulesFileName);
        builtinEnabled = MaskitBuiltinRules.Defaults.ToDictionary(
            item => item.Key, item => item.EnabledByDefault, StringComparer.Ordinal);
        secretPrefixes = [];
        rules = [];
        Load();
    }

    /// <summary>规则变更通知：插件据此让宿主刷新声明式 UI 快照。</summary>
    public event EventHandler? Changed;

    public string DataDirectory { get; }

    /// <summary>规则版本号：每次变更递增，引擎据此重建匹配缓存。</summary>
    public int Version { get; private set; }

    /// <summary>本次进程内累计脱敏项数（只保留计数，不保留任何命中原文）。</summary>
    public long SanitizedCount { get; private set; }

    /// <summary>本次进程内累计还原项数。</summary>
    public long RestoredCount { get; private set; }

    public IReadOnlyList<MaskitRule> Rules
    {
        get { lock (gate) return rules.ToArray(); }
    }

    public IReadOnlyDictionary<string, bool> BuiltinRules
    {
        get { lock (gate) return new Dictionary<string, bool>(builtinEnabled, StringComparer.Ordinal); }
    }

    public IReadOnlyList<string> SecretPrefixes
    {
        get { lock (gate) return secretPrefixes.ToArray(); }
    }

    public void RecordSanitized(int count)
    {
        if (count <= 0) return;
        lock (gate) SanitizedCount += count;
    }

    public void RecordRestored(int count)
    {
        if (count <= 0) return;
        lock (gate) RestoredCount += count;
    }

    /// <summary>新增自定义规则；校验失败或重复时返回 false 并给出可展示的原因。</summary>
    public bool AddRule(
        string category,
        string value,
        MaskitRuleKind kind,
        bool wholeWord,
        out MaskitRule? rule,
        out string? error)
    {
        rule = null;
        if (!TryNormalize(category, value, kind, out var normalizedCategory, out var normalizedValue, out error))
            return false;

        lock (gate)
        {
            if (rules.Count >= MaxRules)
            {
                error = "TOO_MANY_RULES";
                return false;
            }
            if (rules.Any(item => string.Equals(item.Category, normalizedCategory, StringComparison.Ordinal)
                && string.Equals(item.Value, normalizedValue, StringComparison.Ordinal)
                && item.Kind == kind))
            {
                error = "DUPLICATE_RULE";
                return false;
            }

            rule = new MaskitRule(
                $"custom.{kind.ToString().ToLowerInvariant()}.{++sequence}",
                normalizedCategory,
                normalizedValue,
                kind,
                wholeWord);
            rules.Add(rule);
            Save();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>更新自定义规则；内置规则不在此表中，不受影响。</summary>
    public bool UpdateRule(string ruleId, string category, string value, bool wholeWord, out string? error)
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
            if (!TryNormalize(
                    category, value, existing.Kind,
                    out var normalizedCategory, out var normalizedValue, out error))
                return false;

            if (rules.Any(item => item.Id != ruleId
                && string.Equals(item.Category, normalizedCategory, StringComparison.Ordinal)
                && string.Equals(item.Value, normalizedValue, StringComparison.Ordinal)
                && item.Kind == existing.Kind))
            {
                error = "DUPLICATE_RULE";
                return false;
            }

            rules[index] = existing with
            {
                Category = normalizedCategory,
                Value = normalizedValue,
                WholeWord = wholeWord,
            };
            Save();
        }

        RaiseChanged();
        return true;
    }

    public bool RemoveRule(string ruleId)
    {
        bool removed;
        lock (gate)
        {
            removed = rules.RemoveAll(item => item.Id == ruleId) > 0;
            if (removed) Save();
        }
        if (removed) RaiseChanged();
        return removed;
    }

    public bool SetRuleEnabled(string ruleId, bool enabled)
    {
        bool changed;
        lock (gate)
        {
            var index = rules.FindIndex(item => item.Id == ruleId);
            if (index < 0) return false;
            if (rules[index].Enabled == enabled) return false;
            rules[index] = rules[index] with { Enabled = enabled };
            Save();
            changed = true;
        }
        if (changed) RaiseChanged();
        return changed;
    }

    public bool SetBuiltinEnabled(string key, bool enabled)
    {
        bool changed;
        lock (gate)
        {
            if (!builtinEnabled.ContainsKey(key)) return false;
            if (builtinEnabled[key] == enabled) return false;
            builtinEnabled[key] = enabled;
            Save();
            changed = true;
        }
        if (changed) RaiseChanged();
        return changed;
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
        bool removed;
        lock (gate)
        {
            removed = secretPrefixes.RemoveAll(item => string.Equals(item, prefix, StringComparison.Ordinal)) > 0;
            if (removed) Save();
        }
        if (removed) RaiseChanged();
        return removed;
    }

    /// <summary>供引擎取用的启用态内置规则（按基线顺序）。</summary>
    public IReadOnlyList<MaskitBuiltinRule> EnabledBuiltinRules()
    {
        var enabled = BuiltinRules;
        return MaskitBuiltinRules.Defaults
            .Where(item => enabled.TryGetValue(item.Key, out var on) ? on : item.EnabledByDefault)
            .ToArray();
    }

    public IReadOnlyList<MaskitRule> EnabledRules() => Rules.Where(item => item.Enabled).ToArray();

    private static bool TryNormalize(
        string? category,
        string? value,
        MaskitRuleKind kind,
        out string normalizedCategory,
        out string normalizedValue,
        out string? error)
    {
        normalizedCategory = (category ?? string.Empty).Trim();
        normalizedValue = (value ?? string.Empty).Trim();
        error = null;

        if (normalizedCategory.Length is 0 or > MaxCategoryLength)
        {
            error = "INVALID_CATEGORY";
            return false;
        }
        if (normalizedValue.Length == 0
            || normalizedValue.Length > (kind == MaskitRuleKind.Pattern ? MaxPatternLength : MaxWordLength))
        {
            error = "INVALID_VALUE";
            return false;
        }
        if (kind == MaskitRuleKind.Pattern && !IsValidRegex(normalizedValue))
        {
            error = "INVALID_PATTERN";
            return false;
        }
        return true;
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void Load()
    {
        if (!File.Exists(rulesFilePath)) return;
        try
        {
            var dto = JsonSerializer.Deserialize<RulesFileDto>(File.ReadAllText(rulesFilePath), StoreJsonOptions);
            if (dto is null) return;

            rules = (dto.Rules ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                    && !string.IsNullOrWhiteSpace(item.Category)
                    && !string.IsNullOrWhiteSpace(item.Value))
                .Select(item => new MaskitRule(
                    item.Id!,
                    item.Category!,
                    item.Value!,
                    item.Kind,
                    item.WholeWord,
                    item.Enabled))
                .ToList();

            foreach (var (key, enabled) in dto.BuiltinRules ?? [])
            {
                if (builtinEnabled.ContainsKey(key)) builtinEnabled[key] = enabled;
            }

            secretPrefixes = (dto.SecretPrefixes ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToList();

            sequence = rules
                .Select(item => item.Id.StartsWith("custom.", StringComparison.Ordinal) ? item.Id : string.Empty)
                .Select(id => id.Split('.').Length == 3 && int.TryParse(id.Split('.')[2], out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // 规则文件损坏：回退到空自定义规则与内置基线，不阻断插件加载。
            rules = [];
            secretPrefixes = [];
        }
    }

    private void Save()
    {
        var dto = new RulesFileDto
        {
            Version = 1,
            Rules = rules.Select(item => new RuleDto
            {
                Id = item.Id,
                Category = item.Category,
                Value = item.Value,
                Kind = item.Kind,
                WholeWord = item.WholeWord,
                Enabled = item.Enabled,
            }).ToList(),
            BuiltinRules = new Dictionary<string, bool>(builtinEnabled, StringComparer.Ordinal),
            SecretPrefixes = [.. secretPrefixes],
        };

        var tempPath = rulesFilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(dto, StoreJsonOptions));
        File.Move(tempPath, rulesFilePath, overwrite: true);
        Version++;
    }

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
        public string? Category { get; set; }
        public string? Value { get; set; }
        public MaskitRuleKind Kind { get; set; }
        public bool WholeWord { get; set; } = true;
        public bool Enabled { get; set; } = true;
    }
}
