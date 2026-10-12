using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LoomX.CredentialProtection;

/// <summary>检测命中的安全摘要：只含规则标识，不含命中的原始内容。</summary>
public sealed record DetectionHit(string RuleId, string Kind);

/// <summary>
/// 凭据检测与脱敏引擎。检测语义迁移自宿主 旧助手保护基线
/// （敏感名称集 + 值形态正则 + 自由文本内容检测），规则来自 Plugin-owned 规则存储。
/// </summary>
public class CredentialEngine
{
    /// <summary>结构化占位符前缀；随机后缀不可用于推导原始值。</summary>
    public const string PlaceholderPrefix = "{{LOOMX_CREDENTIAL_";

    private static readonly Regex PlaceholderPattern = new(
        @"\{\{[ \t\r\n]*L[ \t\r\n]*O[ \t\r\n]*O[ \t\r\n]*M[ \t\r\n]*X[ \t\r\n]*_[ \t\r\n]*C[ \t\r\n]*R[ \t\r\n]*E[ \t\r\n]*D[ \t\r\n]*E[ \t\r\n]*N[ \t\r\n]*T[ \t\r\n]*I[ \t\r\n]*A[ \t\r\n]*L[ \t\r\n]*_[ \t\r\n]*(?<id>(?:[A-Z2-7][ \t\r\n]*){20})\}[ \t\r\n]*\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public const string IntegrityInstruction =
        "LoomX 凭据引用（形如 {{LOOMX_CREDENTIAL_...}}）是不透明且不可变的本地引用。" +
        "你可以根据目标 JSON、Header、URL、Shell 或工具调用语法放置完整引用，" +
        "但必须逐字符原样复制引用本身，不得修改大小写、增删空白、拆分、转义、翻译、猜测或重新格式化。";
    private static readonly string[] HeaderContainerNames = ["headers", "custom_headers", "http_headers"];

    private readonly SensitiveRuleStore store;
    private readonly CredentialTokenStore tokenStore;
    private readonly AsyncLocal<CancellationToken> operationCancellation = new();

    /// <summary>
    /// 已编译规则的不可变快照。
    /// MUST 整体替换而非就地修改：多线程脱敏时若在遍历过程中被另一个线程改写，
    /// 会导致同一段明文被重复替换，产出嵌套 placeholder（安全缺陷）。
    /// </summary>
    private sealed record CompiledRules(
        int Version,
        HashSet<string> SensitiveNames,
        List<(string RuleId, Regex Pattern)> Patterns,
        List<(string RuleId, Regex Pattern)> BuiltinPatterns,
        List<string> Prefixes)
    {
        public static CompiledRules Initial { get; } = new(
            -1,
            new HashSet<string>(StringComparer.Ordinal),
            [],
            [],
            []);
    }

    private CompiledRules compiled = CompiledRules.Initial;
    private readonly object compilationGate = new();

    public CredentialEngine(SensitiveRuleStore store, CredentialTokenStore tokenStore)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        ArgumentNullException.ThrowIfNull(tokenStore);
        this.tokenStore = tokenStore;
    }

    // 同一引擎可并发服务请求；取消令牌只在本次同步解析作用域内流动。
    internal T WithCancellation<T>(CancellationToken cancellationToken, Func<T> operation)
    {
        var previous = operationCancellation.Value;
        operationCancellation.Value = cancellationToken;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = operation();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { operationCancellation.Value = previous; }
    }

    /// <summary>
    /// 主动保护开关。停用只停止新明文的检测与 token 化，
    /// 历史会话中已签发的 placeholder 仍必须注入完整性指令并完成恢复。
    /// </summary>
    public bool ActiveProtectionEnabled { get; set; } = true;

    /// <summary>检测payload中的敏感内容，返回安全摘要（不含原始值）。</summary>
    public virtual IReadOnlyList<DetectionHit> Detect(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return [];
        var rules = EnsureCompiled();

        var hits = new List<DetectionHit>();
        var root = TryParseJson(payload, out var consumed);
        if (root is not null && consumed)
        {
            DetectNode(root, rules, hits);
        }
        else
        {
            DetectText(payload, rules, hits);
        }

        return hits;
    }

    /// <summary>
    /// 脱敏：敏感名称字段值与值形态命中替换为本地可恢复的结构化占位符；
    /// 同一引擎生命周期内相同原值复用同一个 token，无敏感内容时原样返回。
    /// </summary>
    public virtual string Sanitize(string? payload, out bool changed) =>
        Sanitize(payload, out changed, out _);

    /// <summary>脱敏并返回实际替换位置数量；重复命中按实际替换次数累计。</summary>
    public virtual string Sanitize(string? payload, out bool changed, out int replacementCount)
    {
        changed = false;
        replacementCount = 0;
        if (string.IsNullOrWhiteSpace(payload)) return payload ?? string.Empty;

        // 停用只停止主动保护：不做新明文检测与 token 化。
        // 恢复路径（Restore*）与完整性指令注入不受此开关影响。
        if (!ActiveProtectionEnabled) return payload;

        var rules = EnsureCompiled();

        var root = TryParseJson(payload, out var consumed);
        if (root is not null && consumed)
        {
            var sanitized = SanitizeNode(
                root,
                rules,
                isSensitivePath: false,
                ref changed,
                ref replacementCount);
            return changed ? sanitized.ToJsonString() : payload;
        }

        return SanitizeText(payload, rules, ref changed, ref replacementCount);
    }

    /// <summary>
    /// 恢复普通文本中的本地占位符。仅恢复本引擎签发且仍在有效期内的 token。
    /// </summary>
    public virtual string Restore(string? payload, out bool changed) =>
        RestoreCore(payload, out changed);

    /// <summary>
    /// 恢复 Provider JSON/SSE 传输正文中的占位符。JSON 字符串先解码、恢复再重新序列化，
    /// 避免原值中的引号、反斜杠或换行破坏响应帧与工具参数 JSON；非 JSON 文本按普通文本恢复。
    /// </summary>
    public virtual string RestoreTransportPayload(string? payload, out bool changed)
    {
        changed = false;
        if (string.IsNullOrEmpty(payload)) return payload ?? string.Empty;

        if (TryRestoreJson(payload, out var restoredJson, out changed)) return restoredJson;
        if (TryRestoreSse(payload, out var restoredSse, out changed)) return restoredSse;
        return RestoreCore(payload, out changed);
    }

    private string RestoreCore(string? payload, out bool changed)
    {
        changed = false;
        if (string.IsNullOrEmpty(payload)) return payload ?? string.Empty;

        var candidates = PlaceholderPattern.Matches(payload)
            .Select(match => new PlaceholderCandidate(match.Value, NormalizePlaceholder(match)))
            .ToArray();
        var snapshot = CredentialSynchronous.Run(() => tokenStore.ResolveTokensAsync(candidates.Select(candidate => candidate.Normalized), operationCancellation.Value));
        if (snapshot.Count == 0) return payload;

        var didChange = false;
        var restored = PlaceholderPattern.Replace(payload, match =>
        {
            var normalized = NormalizePlaceholder(match);
            if (!snapshot.TryGetValue(normalized, out var original)) return match.Value;
            didChange = true;
            return original;
        });
        changed = didChange;
        return restored;
    }

    public static bool ContainsPlaceholderCandidate(string? payload) =>
        !string.IsNullOrEmpty(payload) && PlaceholderPattern.IsMatch(payload);

    private static string NormalizePlaceholder(Match match)
    {
        var id = string.Concat(match.Groups["id"].Value.Where(character => !IsAsciiWhitespace(character)))
            .ToUpperInvariant();
        return PlaceholderPrefix + id + "}}";
    }

    private static bool IsAsciiWhitespace(char character) => character is ' ' or '\t' or '\r' or '\n';

    private sealed record PlaceholderCandidate(string Original, string Normalized);

    private bool TryRestoreJson(string payload, out string restored, out bool changed)
    {
        restored = payload;
        changed = false;
        JsonNode? node;
        try { node = JsonNode.Parse(payload); }
        catch (JsonException) { return false; }
        if (node is null) return false;

        if (node is JsonValue rootValue && rootValue.TryGetValue<string>(out var rootText))
        {
            var rootRestored = RestoreCore(rootText, out changed);
            if (changed) restored = JsonSerializer.Serialize(rootRestored);
            return true;
        }

        changed = RestoreJsonNode(node);
        if (changed) restored = node.ToJsonString();
        return true;
    }

    private bool TryRestoreSse(string payload, out string restored, out bool changed)
    {
        restored = payload;
        changed = false;
        if (!payload.Contains("data:", StringComparison.OrdinalIgnoreCase)) return false;

        var lines = payload.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var carriageReturn = line.EndsWith('\r') ? "\r" : string.Empty;
            var content = carriageReturn.Length == 0 ? line : line[..^1];
            if (!content.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

            var data = content[5..];
            var leadingWhitespaceLength = data.Length - data.TrimStart().Length;
            var json = data.Trim();
            if (json.Length == 0 || string.Equals(json, "[DONE]", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryRestoreJson(json, out var restoredJson, out var lineChanged) || !lineChanged) continue;

            lines[index] = content[..5] + data[..leadingWhitespaceLength] + restoredJson + carriageReturn;
            changed = true;
        }

        if (changed) restored = string.Join('\n', lines);
        return true;
    }

    private string RestoreEmbeddedJson(string text, out bool changed)
    {
        if (TryRestoreJson(text, out var restored, out changed)) return restored;
        return RestoreCore(text, out changed);
    }

    private bool RestoreJsonNode(JsonNode node)
    {
        var changed = false;
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var restored = string.Equals(property.Key, "arguments", StringComparison.OrdinalIgnoreCase)
                        ? RestoreEmbeddedJson(text, out var valueChanged)
                        : RestoreCore(text, out valueChanged);
                    if (valueChanged)
                    {
                        jsonObject[property.Key] = restored;
                        changed = true;
                    }
                }
                else if (property.Value is not null)
                {
                    changed |= RestoreJsonNode(property.Value);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (var index = 0; index < jsonArray.Count; index++)
            {
                if (jsonArray[index] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var restored = RestoreCore(text, out var valueChanged);
                    if (valueChanged)
                    {
                        jsonArray[index] = restored;
                        changed = true;
                    }
                }
                else if (jsonArray[index] is not null)
                {
                    changed |= RestoreJsonNode(jsonArray[index]!);
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// 返回当前规则版本对应的已编译快照。版本命中时直接复用，否则加锁重建。
    /// 重建结果以不可变快照整体发布，读侧无需加锁且不会读到中间态。
    /// </summary>
    private CompiledRules EnsureCompiled()
    {
        var version = store.Version;
        var snapshot = compiled;
        if (snapshot.Version == version) return snapshot;

        lock (compilationGate)
        {
            if (compiled.Version == version) return compiled;

            var names = new HashSet<string>(StringComparer.Ordinal);
            var patterns = new List<(string, Regex)>();
            var builtin = new List<(string, Regex)>();
            var prefixes = new List<string>();

            foreach (var rule in store.Rules)
            {
                if (!rule.Enabled) continue;
                if (rule.Kind == SensitiveRuleKind.Name)
                {
                    names.Add(NormalizeSegment(rule.Value));
                }
                else
                {
                    var compiledPattern = TryCompile(rule.Id, rule.Value);
                    if (compiledPattern is not null) patterns.Add((rule.Id, compiledPattern));
                }
            }

            // 内置形态规则：可开关，默认顺序即脱敏顺序（先窄后宽）。
            foreach (var rule in store.EnabledBuiltinRules())
            {
                var compiledPattern = TryCompile($"builtin.{rule.Key}", rule.Pattern);
                if (compiledPattern is not null) builtin.Add(($"builtin.{rule.Key}", compiledPattern));
            }

            // 自定义密钥前缀：命中此前缀的字面片段一律替换。
            prefixes.AddRange(store.SecretPrefixes);

            // 一次性发布不可变快照。
            var rebuilt = new CompiledRules(version, names, patterns, builtin, prefixes);
            compiled = rebuilt;
            return rebuilt;
        }
    }

    private static Regex? TryCompile(string ruleId, string pattern)
    {
        try
        {
            return new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException)
        {
            // 非法正则规则跳过（用户自定义规则可能写错），不影响其余规则。
            return null;
        }
    }

    private void DetectNode(JsonNode node, CompiledRules rules, List<DetectionHit> hits)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    if (IsSensitiveSegment(property.Key, rules))
                    {
                        hits.Add(new DetectionHit($"name.{NormalizeSegment(property.Key)}", "name"));
                        if (property.Value is not null)
                            DetectNode(property.Value, rules, hits);
                    }
                    else if (property.Value is not null)
                    {
                        DetectNode(property.Value, rules, hits);
                    }
                }
                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                    if (item is not null)
                        DetectNode(item, rules, hits);
                break;
            case JsonValue value:
                if (value.TryGetValue<string>(out var text))
                    DetectText(text, rules, hits);
                break;
        }
    }

    private static void DetectText(string text, CompiledRules rules, List<DetectionHit> hits)
    {
        foreach (var (ruleId, pattern) in rules.Patterns)
        {
            if (pattern.IsMatch(text))
                hits.Add(new DetectionHit(ruleId, "pattern"));
        }

        foreach (var (ruleId, pattern) in rules.BuiltinPatterns)
        {
            if (pattern.IsMatch(text))
                hits.Add(new DetectionHit(ruleId, "builtin"));
        }

        foreach (var prefix in rules.Prefixes)
        {
            if (text.Contains(prefix, StringComparison.Ordinal))
                hits.Add(new DetectionHit("prefix.custom", "prefix"));
        }

        if (ContainsSensitiveWords(text))
            hits.Add(new DetectionHit("content.sensitive-words", "content"));
    }

    private JsonNode SanitizeNode(
        JsonNode node,
        CompiledRules rules,
        bool isSensitivePath,
        ref bool changed,
        ref int replacementCount)
    {
        switch (node)
        {
            case JsonObject jsonObject:
            {
                var result = new JsonObject();
                foreach (var property in jsonObject)
                {
                    if (property.Value is null)
                    {
                        result[property.Key] = null;
                        continue;
                    }

                    // 敏感名称字段：值整体视为敏感（与 旧助手保护基线 路径语义一致，
                    // 覆盖自定义 Header 容器——其子值无论名称全部脱敏）。
                    result[property.Key] = SanitizeNode(
                        property.Value,
                        rules,
                        isSensitivePath || IsSensitiveSegment(property.Key, rules),
                        ref changed,
                        ref replacementCount);
                }

                return result;
            }
            case JsonArray jsonArray:
            {
                var result = new JsonArray();
                foreach (var item in jsonArray)
                    result.Add(item is null
                        ? null
                        : SanitizeNode(item, rules, isSensitivePath, ref changed, ref replacementCount));
                return result;
            }
            case JsonValue value:
            {
                if (isSensitivePath)
                {
                    var original = value.TryGetValue<string>(out var sensitiveText)
                        ? sensitiveText
                        : value.ToJsonString();

                    // 敏感字段的值若本身就是历史凭据引用，逐字符透传。
                    // 再包一层会让这个 token 的原值变成 placeholder 本身，
                    // 历史会话中的凭据引用从此永远无法还原。
                    if (IsPlaceholderOnly(original)) return value.DeepClone();

                    changed = true;
                    replacementCount++;
                    return JsonValue.Create(GetOrCreatePlaceholder(original));
                }

                if (!value.TryGetValue<string>(out var text)) return value.DeepClone();
                var sanitized = SanitizeText(text, rules, ref changed, ref replacementCount);
                return JsonValue.Create(sanitized);
            }
            default:
                return node.DeepClone();
        }
    }

    /// <summary>
    /// 单遍扫描式脱敏：先在原文本上求出全部命中区间，再一次性生成替换结果。
    /// 绝不采用"逐条规则顺序 Replace"——那样后一条规则会在前一条刚生成的
    /// placeholder 上再次命中，而随机后缀有约2% 概率被 IBAN/AKIA 形态规则识别，
    /// 产出 {{LOOMX_CREDENTIAL_{{LOOMX_CREDENTIAL_...}}}} 这类嵌套引用，
    /// 其外层 token 的原值是placeholder 本身，历史会话将永远无法还原。
    /// </summary>
    private string SanitizeText(string text, CompiledRules rules, ref bool changed, ref int replacementCount)
    {
        // 已存在的合法凭据引用受保护：不参与命中计算，原样透传。
        // 旧会话、模型回传、多次重试都会让入参里带着上一轮签发的 placeholder。
        var protectedSpans = PlaceholderPattern.Matches(text)
            .Select(match => (match.Index, match.Length))
            .ToArray();

        var hits = new List<(int Start, int Length)>();
        var covered = new bool[text.Length];

        // 自定义密钥前缀优先：前缀命中意味着后续整段都是密钥材料。
        foreach (var prefix in rules.Prefixes)
        {
            foreach (var (start, length) in FindPrefixSegments(text, prefix))
            {
                if (TryClaim(covered, hits, start, length, protectedSpans)) continue;
            }
        }

        // 形态规则按"先窄后宽"顺序竞争同一段区间：先占者胜，
        // 与历史行为一致（窄类别不再被宽类别切碎）。
        foreach (var (_, pattern) in rules.Patterns.Concat(rules.BuiltinPatterns))
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (TryClaim(covered, hits, match.Index, match.Length, protectedSpans)) continue;
            }
        }

        if (hits.Count == 0) return text;

        var builder = new StringBuilder(text.Length);
        var position = 0;
        foreach (var (start, length) in hits)
        {
            builder.Append(text, position, start - position);
            builder.Append(GetOrCreatePlaceholder(text.Substring(start, length)));
            position = start + length;
        }

        builder.Append(text, position, text.Length - position);
        changed = true;
        replacementCount += hits.Count;
        return builder.ToString();
    }

    /// <summary>
    /// 尝试认领一段区间。返回 false 表示该区间与已认领区间重叠或落在受保护
    /// 的placeholder 上，应放弃本次命中。
    /// </summary>
    private static bool TryClaim(
        bool[] covered,
        List<(int Start, int Length)> hits,
        int start,
        int length,
        (int Index, int Length)[] protectedSpans)
    {
        if (start < 0 || length <= 0) return false;
        var end = start + length;
        if (end > covered.Length) return false;

        for (var index = start; index < end; index++)
            if (covered[index]) return false;

        foreach (var (protectedIndex, protectedLength) in protectedSpans)
        {
            if (protectedIndex >= end) break;
            if (protectedIndex + protectedLength > start) return false;
        }

        for (var index = start; index < end; index++)
            covered[index] = true;

        hits.Add((start, length));
        return true;
    }

    /// <summary>
    /// 找出命中自定义前缀的连续 token 片段（不含分隔空白）的区间。
    /// </summary>
    private static List<(int Start, int Length)> FindPrefixSegments(string text, string prefix)
    {
        var segments = new List<(int, int)>();
        var position = 0;

        while (position < text.Length)
        {
            var index = text.IndexOf(prefix, position, StringComparison.Ordinal);
            if (index < 0) break;

            var segmentEnd = text.Length;
            for (var cursor = index + prefix.Length; cursor < text.Length; cursor++)
            {
                if (char.IsWhiteSpace(text[cursor]) || text[cursor] is ',' or ';' or '"' or '\'')
                {
                    segmentEnd = cursor;
                    break;
                }
            }

            segments.Add((index, segmentEnd - index));
            position = segmentEnd;
        }

        return segments;
    }

    /// <summary>整段文本是否恰好是一个合法凭据引用（可带首尾空白）。</summary>
    private static bool IsPlaceholderOnly(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        foreach (Match match in PlaceholderPattern.Matches(trimmed))
        {
            if (match.Index == 0 && match.Length == trimmed.Length) return true;
        }

        return false;
    }

    private string GetOrCreatePlaceholder(string original) =>
        CredentialSynchronous.Run(() => tokenStore.GetOrCreateTokenAsync(
            original,
            () => PlaceholderPrefix + CreateRandomSuffix() + "}}", operationCancellation.Value));

    private static string CreateRandomSuffix()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        Span<byte> bytes = stackalloc byte[20];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        Span<char> suffix = stackalloc char[20];
        for (var index = 0; index < suffix.Length; index++)
            suffix[index] = alphabet[bytes[index] & 31];
        return new string(suffix);
    }

    private bool IsSensitiveSegment(string segment, CompiledRules rules)
    {
        if (string.IsNullOrWhiteSpace(segment)) return false;
        var normalized = NormalizeSegment(segment);
        if (rules.SensitiveNames.Contains(normalized)) return true;
        return rules.SensitiveNames.Any(name => normalized.EndsWith($"_{name}", StringComparison.Ordinal));
    }

    private static string NormalizeSegment(string segment) =>
        segment.Trim().ToLowerInvariant().Replace('-', '_');

    private static JsonNode? TryParseJson(string payload, out bool consumed)
    {
        consumed = false;
        var trimmed = payload.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is not ('{' or '[')) return null;
        try
        {
            var node = JsonNode.Parse(payload);
            consumed = node is JsonObject or JsonArray;
            return consumed ? node : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>自由文本内容检测：语义与 旧助手保护基线.ContainsSensitiveContent 的词汇规则一致。</summary>
    private static bool ContainsSensitiveWords(string content)
    {
        var words = SplitContentWords(content);
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var next = index + 1 < words.Count ? words[index + 1] : null;
            if (word is "authorization" or "authorizations"
                or "credential" or "credentials"
                or "secret" or "secrets"
                or "bearer")
            {
                return true;
            }

            if (word is "password" or "passwords" or "passwd" && next is not "policy" and not "policies")
            {
                return true;
            }

            if (word is "key" or "keys" or "token" or "tokens")
            {
                return true;
            }

            if (next is "key" or "keys" && word is "api" or "access" or "private" or "secret")
            {
                return true;
            }

            if (next is "token" or "tokens" && word is "access" or "refresh" or "auth" or "id")
            {
                return true;
            }

            if (word == "client" && next is "secret" or "secrets")
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> SplitContentWords(string content)
    {
        var normalized = new StringBuilder(content.Length * 2);
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (char.IsUpper(character)
                && index > 0
                && (char.IsLower(content[index - 1]) || char.IsDigit(content[index - 1])))
            {
                normalized.Append(' ');
            }

            normalized.Append(char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : ' ');
        }

        return normalized
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
