using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoomX.ToolResultCompression;

/// <summary>压缩强度预设的标识；与文案解耦，命令回传时按文案反查。</summary>
public static class CompressionLevel
{
    public const string Conservative = "conservative";
    public const string Standard = "standard";
    public const string Aggressive = "aggressive";
}

/// <summary>
/// 工具结果压缩策略。数值均做安全钳制：过大的上限退化为默认，过小的下限保证不破坏可读性。
/// </summary>
public sealed record ToolResultCompressionOptions
{
    /// <summary>总开关；关闭时插件原样放行请求正文。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>小于该字符数的工具结果不压缩，避免为短结果引入噪音。</summary>
    public int MinChars { get; init; } = 1200;

    /// <summary>压缩后仍需达到的最小节省比例，否则保留原文。</summary>
    public double MinSavingsRatio { get; init; } = 0.15;

    /// <summary>单次请求末尾保留原文的工具结果条数；最近的结果最可能被继续引用。</summary>
    public int KeepRecentToolResults { get; init; } = 2;

    /// <summary>行数上限；超出后保留头尾并省略中间。</summary>
    public int MaxLines { get; init; } = 160;

    /// <summary>头尾截断时保留的头部行数。</summary>
    public int HeadLines { get; init; } = 80;

    /// <summary>头尾截断时保留的尾部行数。</summary>
    public int TailLines { get; init; } = 40;

    /// <summary>单行字符上限；超出后截断并标注省略量。</summary>
    public int MaxLineLength { get; init; } = 400;

    /// <summary>JSON 数组最多保留的项数。</summary>
    public int JsonMaxItems { get; init; } = 25;

    /// <summary>JSON 字符串值最多保留的字符数。</summary>
    public int JsonMaxStringLength { get; init; } = 600;

    /// <summary>JSON 折叠的最大深度；更深层级折叠为占位说明。</summary>
    public int JsonMaxDepth { get; init; } = 6;

    /// <summary>是否清理 ANSI 转义、回车与冗余空行。</summary>
    public bool EnableNoiseFilter { get; init; } = true;

    /// <summary>是否折叠重复行。</summary>
    public bool EnableDedupe { get; init; } = true;

    /// <summary>是否启用 git / 测试 / 目录列表等命令输出专用过滤。</summary>
    public bool EnableCommandFilters { get; init; } = true;

    /// <summary>是否启用 JSON 结构化折叠。</summary>
    public bool EnableJsonFolding { get; init; } = true;

    /// <summary>连续重复行达到该次数才折叠。</summary>
    public int DedupeRepeatThreshold { get; init; } = 2;

    /// <summary>非连续重复行达到该总次数才折叠为计数。</summary>
    public int DedupeGlobalThreshold { get; init; } = 3;

    public static ToolResultCompressionOptions Default { get; } = new();

    /// <summary>保守：只压明显冗长的输出，保真窗口更大。</summary>
    public static ToolResultCompressionOptions ConservativePreset { get; } = new()
    {
        MinChars = 2400,
        MinSavingsRatio = 0.30,
        KeepRecentToolResults = 3,
        MaxLines = 260,
        HeadLines = 140,
        TailLines = 60,
        MaxLineLength = 800,
        JsonMaxItems = 40,
        JsonMaxStringLength = 1200,
    };

    /// <summary>激进：更早上手压缩、保真窗口更小，适合长会话。</summary>
    public static ToolResultCompressionOptions AggressivePreset { get; } = new()
    {
        MinChars = 600,
        MinSavingsRatio = 0.08,
        KeepRecentToolResults = 1,
        MaxLines = 100,
        HeadLines = 50,
        TailLines = 25,
        MaxLineLength = 240,
        JsonMaxItems = 15,
        JsonMaxStringLength = 320,
    };

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>按强度标识取预设；未知标识回退标准。</summary>
    public static ToolResultCompressionOptions FromLevel(string? level) => level switch
    {
        CompressionLevel.Conservative => ConservativePreset,
        CompressionLevel.Aggressive => AggressivePreset,
        _ => Default,
    };

    /// <summary>
    /// 反查当前配置所属的强度档位。数值被手动改过就不再属于任何预设，
    /// 此时返回空串，UI 展示为「自定义」。
    /// </summary>
    public string MatchLevel()
    {
        if (Matches(ConservativePreset)) return CompressionLevel.Conservative;
        if (Matches(Default)) return CompressionLevel.Standard;
        if (Matches(AggressivePreset)) return CompressionLevel.Aggressive;
        return string.Empty;
    }

    private bool Matches(ToolResultCompressionOptions preset) =>
        MinChars == preset.MinChars
        && MinSavingsRatio.Equals(preset.MinSavingsRatio)
        && KeepRecentToolResults == preset.KeepRecentToolResults
        && MaxLines == preset.MaxLines
        && HeadLines == preset.HeadLines
        && TailLines == preset.TailLines
        && MaxLineLength == preset.MaxLineLength
        && JsonMaxItems == preset.JsonMaxItems
        && JsonMaxStringLength == preset.JsonMaxStringLength;

    /// <summary>钳制非法配置，保证压缩器不会因为配置错误产生越界行为。</summary>
    public ToolResultCompressionOptions Normalized() => new()
    {
        Enabled = Enabled,
        MinChars = Math.Clamp(MinChars, 0, 1_000_000),
        MinSavingsRatio = Math.Clamp(MinSavingsRatio, 0, 0.95),
        KeepRecentToolResults = Math.Clamp(KeepRecentToolResults, 0, 32),
        MaxLines = Math.Clamp(MaxLines, 4, 20_000),
        HeadLines = Math.Clamp(HeadLines, 1, 20_000),
        TailLines = Math.Clamp(TailLines, 0, 20_000),
        MaxLineLength = Math.Clamp(MaxLineLength, 40, 20_000),
        JsonMaxItems = Math.Clamp(JsonMaxItems, 1, 5_000),
        JsonMaxStringLength = Math.Clamp(JsonMaxStringLength, 40, 100_000),
        JsonMaxDepth = Math.Clamp(JsonMaxDepth, 1, 64),
        EnableNoiseFilter = EnableNoiseFilter,
        EnableDedupe = EnableDedupe,
        EnableCommandFilters = EnableCommandFilters,
        EnableJsonFolding = EnableJsonFolding,
        DedupeRepeatThreshold = Math.Clamp(DedupeRepeatThreshold, 2, 1_000),
        DedupeGlobalThreshold = Math.Clamp(DedupeGlobalThreshold, 2, 10_000),
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static ToolResultCompressionOptions? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ToolResultCompressionOptions>(json, SerializerOptions)?.Normalized();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
