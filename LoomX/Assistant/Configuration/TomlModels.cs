using System.Collections.ObjectModel;
using System.Text.Json;

namespace LoomX.Assistant.Configuration;

public readonly record struct TomlPath
{
    public TomlPath(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            throw new ArgumentException("TOML 路径不能为空。", nameof(segments));
        }

        var copy = new string[segments.Count];
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (string.IsNullOrWhiteSpace(segment))
            {
                throw new ArgumentException("TOML 路径片段不能为空。", nameof(segments));
            }

            copy[index] = segment;
        }

        Segments = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<string> Segments { get; }

    public bool Equals(TomlPath other)
    {
        if (Segments is null || other.Segments is null)
        {
            return Segments is null && other.Segments is null;
        }

        return Segments.SequenceEqual(other.Segments, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        if (Segments is null)
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var segment in Segments)
        {
            hash.Add(segment, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

public enum TomlValueKind
{
    String,
    Integer,
    Float,
    Boolean,
    Array,
    Object,
}

public sealed record TomlValue
{
    private TomlValue(TomlValueKind kind, object value)
    {
        Kind = kind;
        Value = value;
    }

    public TomlValueKind Kind { get; }

    public object Value { get; }

    public static TomlValue FromObject(object? value) => value switch
    {
        null => throw new ArgumentException("TOML 值不支持 null。", nameof(value)),
        TomlValue tomlValue => tomlValue,
        string text => new TomlValue(TomlValueKind.String, text),
        sbyte integer => new TomlValue(TomlValueKind.Integer, (long)integer),
        short integer => new TomlValue(TomlValueKind.Integer, (long)integer),
        int integer => new TomlValue(TomlValueKind.Integer, (long)integer),
        long integer => new TomlValue(TomlValueKind.Integer, integer),
        double number when double.IsFinite(number) => new TomlValue(TomlValueKind.Float, number),
        double => throw new ArgumentException("TOML 浮点值必须是有限 double。", nameof(value)),
        bool boolean => new TomlValue(TomlValueKind.Boolean, boolean),
        IReadOnlyList<TomlValue> items => FromArray(items),
        IReadOnlyDictionary<string, TomlValue> properties => FromObjectProperties(properties),
        IReadOnlyDictionary<string, object?> properties => FromObjectProperties(
            properties.Select(property => new KeyValuePair<string, TomlValue>(property.Key, FromObject(property.Value)))),
        IEnumerable<object?> items => FromArray(items.Select(FromObject)),
        _ => throw new ArgumentException($"不支持将 CLR 类型 {value.GetType().Name} 转换为 TOML 值。", nameof(value)),
    };

    public static TomlValue FromJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => FromObject(element.GetString()!),
        JsonValueKind.Number => FromJsonNumber(element),
        JsonValueKind.True => FromObject(true),
        JsonValueKind.False => FromObject(false),
        JsonValueKind.Array => FromArray(element.EnumerateArray().Select(FromJsonElement)),
        JsonValueKind.Object => FromObjectProperties(
            element.EnumerateObject().Select(property =>
                new KeyValuePair<string, TomlValue>(property.Name, FromJsonElement(property.Value)))),
        JsonValueKind.Null => throw new ArgumentException("TOML 值不支持 JSON null。", nameof(element)),
        _ => throw new ArgumentException($"不支持 JSON 类型 {element.ValueKind}。", nameof(element)),
    };

    internal static TomlValue FromArray(IEnumerable<TomlValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = new List<TomlValue>();
        foreach (var item in items)
        {
            copy.Add(item ?? throw new ArgumentException("TOML 数组元素不能为 null。", nameof(items)));
        }

        return new TomlValue(TomlValueKind.Array, Array.AsReadOnly(copy.ToArray()));
    }

    internal static TomlValue FromObjectProperties(IEnumerable<KeyValuePair<string, TomlValue>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var copy = new Dictionary<string, TomlValue>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (string.IsNullOrEmpty(property.Key))
            {
                throw new ArgumentException("TOML 对象键不能为空。", nameof(properties));
            }

            copy.Add(property.Key, property.Value ?? throw new ArgumentException("TOML 对象值不能为 null。", nameof(properties)));
        }

        return new TomlValue(
            TomlValueKind.Object,
            new ReadOnlyDictionary<string, TomlValue>(copy));
    }

    private static TomlValue FromJsonNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var integer))
        {
            return FromObject(integer);
        }

        var rawText = element.GetRawText();
        if (!rawText.Contains('.') && !rawText.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("JSON 整数超出 Int64 范围。", nameof(element));
        }

        if (element.TryGetDouble(out var number) && double.IsFinite(number))
        {
            return FromObject(number);
        }

        throw new ArgumentException("JSON 数字无法转换为有限 double。", nameof(element));
    }
}

public enum TomlPatchKind
{
    Set,
    Delete,
}

public sealed record TomlPatchOperation
{
    public TomlPatchOperation(TomlPatchKind kind, TomlPath path, TomlValue? value = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定义的 TOML Patch 操作类型。");
        }

        if (path.Segments is null || path.Segments.Count == 0)
        {
            throw new ArgumentException("TOML Patch 路径不能为空。", nameof(path));
        }

        if (kind == TomlPatchKind.Set && value is null)
        {
            throw new ArgumentException("set 操作必须提供值。", nameof(value));
        }

        if (kind == TomlPatchKind.Delete && value is not null)
        {
            throw new ArgumentException("delete 操作不能提供值。", nameof(value));
        }

        Kind = kind;
        Path = path;
        Value = value;
    }

    public TomlPatchKind Kind { get; }

    public TomlPath Path { get; }

    public TomlValue? Value { get; }
}

public sealed record TomlValidationResult
{
    private readonly ReadOnlyCollection<string> errors;

    public TomlValidationResult(bool isValid, int? line, int? column, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Line = line;
        Column = column;
        this.errors = TomlResultErrors.Copy(errors);
    }

    public bool IsValid { get; }

    public int? Line { get; }

    public int? Column { get; }

    public IReadOnlyList<string> Errors => errors;
}

public sealed record TomlValueResult
{
    private readonly ReadOnlyCollection<string> errors;

    public TomlValueResult(bool found, TomlValueKind? valueType, TomlValue? value, IReadOnlyList<string> errors)
    {
        Found = found;
        ValueType = valueType;
        Value = value;
        this.errors = TomlResultErrors.Copy(errors);
    }

    public bool Found { get; }

    public TomlValueKind? ValueType { get; }

    public TomlValue? Value { get; }

    public IReadOnlyList<string> Errors => errors;
}

public sealed record TomlWriteResult
{
    private readonly ReadOnlyCollection<string> errors;

    public TomlWriteResult(
        bool success,
        bool changed,
        string? backupPath,
        bool formattingChanged,
        IReadOnlyList<string> errors)
    {
        Success = success;
        Changed = changed;
        BackupPath = backupPath;
        FormattingChanged = formattingChanged;
        this.errors = TomlResultErrors.Copy(errors);
    }

    public bool Success { get; }

    public bool Changed { get; }

    public string? BackupPath { get; }

    public bool FormattingChanged { get; }

    public IReadOnlyList<string> Errors => errors;
}

file static class TomlResultErrors
{
    public static ReadOnlyCollection<string> Copy(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var copy = new string[errors.Count];
        for (var index = 0; index < errors.Count; index++)
        {
            copy[index] = errors[index]
                ?? throw new ArgumentException("TOML 结果错误项不能为 null。", nameof(errors));
        }

        return Array.AsReadOnly(copy);
    }
}
