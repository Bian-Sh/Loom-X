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
    private int compiledVersion = -1;
    private HashSet<string> sensitiveNames = new(StringComparer.Ordinal);
    private List<(string RuleId, Regex Pattern)> patterns = [];

    public CredentialEngine(SensitiveRuleStore store, CredentialTokenStore? tokenStore = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.tokenStore = tokenStore ?? new CredentialTokenStore(store.DataDirectory);
    }

    /// <summary>检测payload中的敏感内容，返回安全摘要（不含原始值）。</summary>
    public virtual IReadOnlyList<DetectionHit> Detect(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return [];
        EnsureCompiled();

        var hits = new List<DetectionHit>();
        var root = TryParseJson(payload, out var consumed);
        if (root is not null && consumed)
        {
            DetectNode(root, hits);
        }
        else
        {
            DetectText(payload, hits);
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
        EnsureCompiled();

        var root = TryParseJson(payload, out var consumed);
        if (root is not null && consumed)
        {
            var sanitized = SanitizeNode(
                root,
                isSensitivePath: false,
                ref changed,
                ref replacementCount);
            return changed ? sanitized.ToJsonString() : payload;
        }

        return SanitizeText(payload, ref changed, ref replacementCount);
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
        var snapshot = tokenStore.ResolveTokens(candidates.Select(candidate => candidate.Normalized));
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

    private void EnsureCompiled()
    {
        if (compiledVersion == store.Version) return;
        lock (this)
        {
            if (compiledVersion == store.Version) return;
            var names = new HashSet<string>(StringComparer.Ordinal);
            var compiled = new List<(string, Regex)>();
            foreach (var rule in store.Rules)
            {
                if (!rule.Enabled) continue;
                if (rule.Kind == SensitiveRuleKind.Name)
                {
                    names.Add(NormalizeSegment(rule.Value));
                }
                else
                {
                    try
                    {
                        compiled.Add((rule.Id, new Regex(
                            rule.Value,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            TimeSpan.FromSeconds(2))));
                    }
                    catch (ArgumentException)
                    {
                        // 非法正则规则跳过（用户自定义规则可能写错），不影响其余规则。
                    }
                }
            }

            sensitiveNames = names;
            patterns = compiled;
            compiledVersion = store.Version;
        }
    }

    private void DetectNode(JsonNode node, List<DetectionHit> hits)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    if (IsSensitiveSegment(property.Key))
                    {
                        hits.Add(new DetectionHit($"name.{NormalizeSegment(property.Key)}", "name"));
                        if (property.Value is not null)
                            DetectNode(property.Value, hits);
                    }
                    else if (property.Value is not null)
                    {
                        DetectNode(property.Value, hits);
                    }
                }
                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                    if (item is not null)
                        DetectNode(item, hits);
                break;
            case JsonValue value:
                if (value.TryGetValue<string>(out var text))
                    DetectText(text, hits);
                break;
        }
    }

    private void DetectText(string text, List<DetectionHit> hits)
    {
        foreach (var (ruleId, pattern) in patterns)
        {
            if (pattern.IsMatch(text))
                hits.Add(new DetectionHit(ruleId, "pattern"));
        }

        if (ContainsSensitiveWords(text))
            hits.Add(new DetectionHit("content.sensitive-words", "content"));
    }

    private JsonNode SanitizeNode(
        JsonNode node,
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
                        isSensitivePath || IsSensitiveSegment(property.Key),
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
                        : SanitizeNode(item, isSensitivePath, ref changed, ref replacementCount));
                return result;
            }
            case JsonValue value:
            {
                if (isSensitivePath)
                {
                    changed = true;
                    replacementCount++;
                    var original = value.TryGetValue<string>(out var sensitiveText)
                        ? sensitiveText
                        : value.ToJsonString();
                    return JsonValue.Create(GetOrCreatePlaceholder(original));
                }

                if (!value.TryGetValue<string>(out var text)) return value.DeepClone();
                var sanitized = SanitizeText(text, ref changed, ref replacementCount);
                return JsonValue.Create(sanitized);
            }
            default:
                return node.DeepClone();
        }
    }

    private string SanitizeText(string text, ref bool changed, ref int replacementCount)
    {
        var current = text;
        foreach (var (_, pattern) in patterns)
        {
            var patternReplacementCount = 0;
            var next = pattern.Replace(current, match =>
            {
                patternReplacementCount++;
                return GetOrCreatePlaceholder(match.Value);
            });
            if (patternReplacementCount > 0)
            {
                changed = true;
                replacementCount += patternReplacementCount;
            }
            current = next;
        }

        return current;
    }

    private string GetOrCreatePlaceholder(string original) =>
        tokenStore.GetOrCreateToken(
            original,
            () => PlaceholderPrefix + CreateRandomSuffix() + "}}");

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

    private bool IsSensitiveSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return false;
        var normalized = NormalizeSegment(segment);
        if (sensitiveNames.Contains(normalized)) return true;
        return sensitiveNames.Any(name => normalized.EndsWith($"_{name}", StringComparison.Ordinal));
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
