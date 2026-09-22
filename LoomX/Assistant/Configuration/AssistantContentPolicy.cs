using System.Text;
using System.Text.RegularExpressions;

namespace LoomX.Assistant.Configuration;

/// <summary>
/// 助手产品层的内容暴露策略：限制 TOML 本地配置读取，并禁止 AskUser 索取认证信息。
/// 这是内置助手自己的隐私策略，不属于 Router Credential Protection Plugin；二者可共享规则思想，但边界独立。
/// </summary>
public static class AssistantContentPolicy
{
    private const string RedactedPlaceholder = "***";

    private static readonly Regex SecretValuePattern = new(
        @"(?ix)(?<![a-z0-9])(?:sk-(?:proj-)?[a-z0-9_-]{12,}|gh[pousr]_[a-z0-9]{20,}|github_pat_[a-z0-9_]{20,}|xox[baprs]-[a-z0-9-]{10,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{20,}|eyJ[a-z0-9_-]{6,}\.[a-z0-9_-]{6,}\.[a-z0-9_-]{6,})(?![a-z0-9])",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.Ordinal)
    {
        "key",
        "api_key",
        "token",
        "password",
        "secret",
        "authorization",
        "credential",
        "headers",
        "custom_headers",
        "http_headers",
    };

    public static bool IsSensitivePath(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments.Any(IsSensitiveSegment);
    }

    public static bool ContainsSensitiveContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (SecretValuePattern.IsMatch(content))
        {
            return true;
        }

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

            if (word is "password" or "passwords" or "passwd")
            {
                if (next is not "policy" and not "policies")
                {
                    return true;
                }
            }

            if (word is "key" or "keys")
            {
                return true;
            }

            if (word is "token" or "tokens")
            {
                return true;
            }

            if ((next is "key" or "keys")
                && (word is "api" or "access" or "private" or "secret"))
            {
                return true;
            }

            if ((next is "token" or "tokens")
                && (word is "access" or "refresh" or "auth" or "id"))
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

    public static TomlValue Redact(TomlValue value, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(path);

        if (value.Kind == TomlValueKind.Array)
        {
            return RedactArray(value, path);
        }

        if (value.Kind == TomlValueKind.Object)
        {
            return RedactObject(value, path);
        }

        if (IsSensitivePath(path)
            || value.Kind == TomlValueKind.String
            && ContainsSensitiveContent((string)value.Value))
        {
            return TomlValue.FromObject(RedactedPlaceholder);
        }

        return value;
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

    private static bool IsSensitiveSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var normalized = segment.Trim().ToLowerInvariant().Replace('-', '_');
        if (SensitiveNames.Contains(normalized))
        {
            return true;
        }

        return SensitiveNames.Any(name => normalized.EndsWith($"_{name}", StringComparison.Ordinal));
    }

    private static TomlValue RedactArray(TomlValue value, IReadOnlyList<string> path)
    {
        var items = (IReadOnlyList<TomlValue>)value.Value;
        var redacted = items.Select(item => Redact(item, path)).ToArray();
        return redacted.Where((item, index) => !ReferenceEquals(item, items[index])).Any()
            ? TomlValue.FromArray(redacted)
            : value;
    }

    private static TomlValue RedactObject(TomlValue value, IReadOnlyList<string> path)
    {
        var properties = (IReadOnlyDictionary<string, TomlValue>)value.Value;
        var changed = false;
        var redacted = new Dictionary<string, TomlValue>(properties.Count, StringComparer.Ordinal);

        foreach (var property in properties)
        {
            var childPath = path.Concat([property.Key]).ToArray();
            var childValue = Redact(property.Value, childPath);
            changed |= !ReferenceEquals(childValue, property.Value);
            redacted.Add(property.Key, childValue);
        }

        return changed ? TomlValue.FromObjectProperties(redacted) : value;
    }
}
