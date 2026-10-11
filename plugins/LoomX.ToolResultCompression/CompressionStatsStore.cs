using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoomX.ToolResultCompression;

/// <summary>单次压缩记录的安全摘要：只有时间与数量，不含工具结果内容。</summary>
public sealed record CompressionStatsEntry(
    DateTimeOffset At,
    int CompressedToolResults,
    int SavedChars,
    int SavedTokens,
    double SavingsRatio);

/// <summary>
/// 累计压缩统计。字符数是精确值，token 是按 <see cref="CompressionTokenEstimator"/> 口径的估算值。
/// </summary>
public sealed record CompressionStats(
    long TotalRequests = 0,
    long CompressedRequests = 0,
    long CompressedToolResults = 0,
    long OriginalChars = 0,
    long CompressedChars = 0,
    long EstimatedOriginalTokens = 0,
    long EstimatedCompressedTokens = 0,
    long Errors = 0,
    IReadOnlyList<CompressionStatsEntry>? Recent = null)
{
    public static CompressionStats Empty { get; } = new();

    public IReadOnlyList<CompressionStatsEntry> RecentEntries => Recent ?? [];

    public long SavedChars => Math.Max(0, OriginalChars - CompressedChars);

    public long SavedTokens => Math.Max(0, EstimatedOriginalTokens - EstimatedCompressedTokens);

    public double SavingsRatio => OriginalChars <= 0
        ? 0
        : Math.Clamp((double)SavedChars / OriginalChars, 0, 1);

    /// <summary>估算口径下的节省比例；token 数缺失时退化为字符比例。</summary>
    public double TokenSavingsRatio => EstimatedOriginalTokens <= 0
        ? SavingsRatio
        : Math.Clamp((double)SavedTokens / EstimatedOriginalTokens, 0, 1);
}

/// <summary>
/// 统计持久化。数据落在插件自有目录的 stats.json，重启后继续累计，
/// 这样设置页的「节省对比」看的是长期收益，而不是单次运行的瞬时值。
/// </summary>
public sealed class CompressionStatsStore(string dataDirectory)
{
    private const string StatsFileName = "stats.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly object gate = new();
    private readonly string statsPath = Path.Combine(dataDirectory, StatsFileName);

    public string StatsPath => statsPath;

    public CompressionStats Load()
    {
        lock (gate)
        {
            if (!File.Exists(statsPath)) return CompressionStats.Empty;

            string json;
            try
            {
                json = File.ReadAllText(statsPath);
            }
            catch (IOException)
            {
                return CompressionStats.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return CompressionStats.Empty;
            }

            try
            {
                return JsonSerializer.Deserialize<CompressionStats>(json, SerializerOptions)
                       ?? CompressionStats.Empty;
            }
            catch (JsonException)
            {
                return CompressionStats.Empty;
            }
        }
    }

    public void Save(CompressionStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(dataDirectory);
                File.WriteAllText(statsPath, JsonSerializer.Serialize(stats, SerializerOptions));
            }
            catch (IOException)
            {
                // 统计写不进去不影响压缩本身：内存中的累计值仍然有效。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

}
