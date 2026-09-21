using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LoomX.CredentialProtection;

/// <summary>检测命中的安全摘要：只含规则标识，不含命中的原始内容。</summary>
public sealed record DetectionHit(string RuleId, string Kind);

/// <summary>
/// 凭据检测与脱敏引擎。检测语义迁移自宿主 SensitiveKeyPolicy
/// （敏感名称集 + 值形态正则 + 自由文本内容检测），规则来自 Plugin-owned 规则存储。
/// </summary>
public class CredentialEngine
{
    /// <summary>固定占位符：替换结果不含原始值任何片段，也不可逆推。</summary>
    public const string RedactedPlaceholder = "***";

    private static readonly string[] HeaderContainerNames = ["headers", "custom_headers", "http_headers"];

    private readonly SensitiveRuleStore store;
    private int compiledVersion = -1;
    private HashSet<string> sensitiveNames = new(StringComparer.Ordinal);
    private List<(string RuleId, Regex Pattern)> patterns = [];

    public CredentialEngine(SensitiveRuleStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
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
    /// 脱敏：敏感名称字段值与值形态命中统一替换为固定占位符；
    /// 无敏感内容时原样返回（changed = false）。
    /// </summary>
    public virtual string Sanitize(string? payload, out bool changed)
    {
        changed = false;
        if (string.IsNullOrWhiteSpace(payload)) return payload ?? string.Empty;
        EnsureCompiled();

        var root = TryParseJson(payload, out var consumed);
        if (root is not null && consumed)
        {
            var sanitized = SanitizeNode(root, isSensitivePath: false, ref changed);
            return changed ? sanitized.ToJsonString() : payload;
        }

        return SanitizeText(payload, ref changed);
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

    private JsonNode SanitizeNode(JsonNode node, bool isSensitivePath, ref bool changed)
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

                    // 敏感名称字段：值整体视为敏感（与 SensitiveKeyPolicy 路径语义一致，
                    // 覆盖自定义 Header 容器——其子值无论名称全部脱敏）。
                    result[property.Key] = SanitizeNode(
                        property.Value,
                        isSensitivePath || IsSensitiveSegment(property.Key),
                        ref changed);
                }

                return result;
            }
            case JsonArray jsonArray:
            {
                var result = new JsonArray();
                foreach (var item in jsonArray)
                    result.Add(item is null ? null : SanitizeNode(item, isSensitivePath, ref changed));
                return result;
            }
            case JsonValue value:
            {
                if (isSensitivePath)
                {
                    changed = true;
                    return JsonValue.Create(RedactedPlaceholder);
                }

                if (!value.TryGetValue<string>(out var text)) return value.DeepClone();
                var sanitized = SanitizeText(text, ref changed);
                return JsonValue.Create(sanitized);
            }
            default:
                return node.DeepClone();
        }
    }

    private string SanitizeText(string text, ref bool changed)
    {
        var current = text;
        foreach (var (_, pattern) in patterns)
        {
            var next = pattern.Replace(current, RedactedPlaceholder);
            changed |= next != current;
            current = next;
        }

        return current;
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

    /// <summary>自由文本内容检测：语义与 SensitiveKeyPolicy.ContainsSensitiveContent 的词汇规则一致。</summary>
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
