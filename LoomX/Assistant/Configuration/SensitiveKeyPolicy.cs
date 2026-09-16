namespace LoomX.Assistant.Configuration;

public static class SensitiveKeyPolicy
{
    private const string RedactedPlaceholder = "***";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.Ordinal)
    {
        "key",
        "api_key",
        "token",
        "password",
        "secret",
        "authorization",
        "credential",
    };

    public static bool IsSensitivePath(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments.Any(IsSensitiveSegment);
    }

    public static TomlValue Redact(TomlValue value, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(path);

        if (IsSensitivePath(path))
        {
            return TomlValue.FromObject(RedactedPlaceholder);
        }

        return value.Kind switch
        {
            TomlValueKind.Array => RedactArray(value, path),
            TomlValueKind.Object => RedactObject(value, path),
            _ => value,
        };
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
