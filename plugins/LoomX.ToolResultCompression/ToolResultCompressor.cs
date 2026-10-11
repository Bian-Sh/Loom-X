namespace LoomX.ToolResultCompression;

/// <summary>单次压缩的安全摘要：只含字符数与命中的策略，不含任何原始内容。</summary>
public sealed record CompressionReport(
    int OriginalChars,
    int CompressedChars,
    IReadOnlyList<string> Strategies)
{
    public int SavedChars => Math.Max(0, OriginalChars - CompressedChars);

    public double SavingsRatio => OriginalChars <= 0
        ? 0
        : Math.Clamp((double)SavedChars / OriginalChars, 0, 1);

    public static CompressionReport Unchanged(int chars) => new(chars, chars, []);
}

/// <summary>压缩结果：未达到收益阈值时 Changed 为 false，调用方必须保持原文。</summary>
public sealed record ToolResultCompressionResult(string Text, bool Changed, CompressionReport Report);

/// <summary>
/// 工具结果压缩器。策略参考 rtk：智能过滤（噪音/ANSI）、分组（git、测试、目录）、
/// 截断（长行/头尾）、去重（重复行计数），并针对 JSON 结果做结构化折叠。
/// <para>
/// 实现按策略族拆分为多个 partial 文件（编排 / 噪音去重截断 / JSON 折叠 / Git / 命令输出），
/// 对外仍是同一个 <c>ToolResultCompressor</c>，避免单文件膨胀。
/// </para>
/// </summary>
public static partial class ToolResultCompressor
{
    public const string StrategyNoiseFilter = "noise-filter";
    public const string StrategyDedupe = "dedupe";
    public const string StrategyTruncateLines = "truncate-lines";
    public const string StrategyTruncateLongLines = "truncate-long-lines";
    public const string StrategyJsonFold = "json-fold";
    public const string StrategyGitStatus = "git-status";
    public const string StrategyGitLog = "git-log";
    public const string StrategyGitDiff = "git-diff";
    public const string StrategyTestOutput = "test-summary";
    public const string StrategyDirectoryListing = "directory-listing";

    /// <summary>压缩单条工具结果文本；不满足收益阈值时原样返回。</summary>
    public static ToolResultCompressionResult Compress(string? text, ToolResultCompressionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Normalized();

        if (string.IsNullOrEmpty(text))
            return new ToolResultCompressionResult(text ?? string.Empty, false, CompressionReport.Unchanged(text?.Length ?? 0));

        if (!settings.Enabled || text.Length < settings.MinChars)
            return new ToolResultCompressionResult(text, false, CompressionReport.Unchanged(text.Length));

        var strategies = new List<string>();
        var current = text;

        if (settings.EnableNoiseFilter && Replace(ref current, RemoveNoise(current)))
            strategies.Add(StrategyNoiseFilter);

        // JSON 折叠与命令输出过滤互斥：两者都会重写整段结构，叠加会互相破坏语义。
        if (settings.EnableJsonFolding && TryFoldJson(current, settings, out var folded))
        {
            strategies.Add(StrategyJsonFold);
            current = folded;
        }
        else if (settings.EnableCommandFilters
                 && TryApplyCommandFilter(current, settings, out var filtered, out var commandStrategy))
        {
            strategies.Add(commandStrategy);
            current = filtered;
        }

        if (settings.EnableDedupe && Replace(ref current, DedupeLines(current, settings)))
            strategies.Add(StrategyDedupe);

        if (Replace(ref current, TruncateLongLines(current, settings)))
            strategies.Add(StrategyTruncateLongLines);

        if (Replace(ref current, TruncateLines(current, settings)))
            strategies.Add(StrategyTruncateLines);

        // 收益判定放在最后：压缩后不更短、或节省比例不足，一律保留原文，不为压缩而压缩。
        if (current.Length >= text.Length)
            return new ToolResultCompressionResult(text, false, CompressionReport.Unchanged(text.Length));

        var report = new CompressionReport(text.Length, current.Length, strategies);
        return report.SavingsRatio < settings.MinSavingsRatio
            ? new ToolResultCompressionResult(text, false, CompressionReport.Unchanged(text.Length))
            : new ToolResultCompressionResult(current, true, report);
    }

    /// <summary>候选文本与当前文本不同才替换并返回 true，用于统一采集命中的策略。</summary>
    private static bool Replace(ref string current, string candidate)
    {
        if (string.Equals(candidate, current, StringComparison.Ordinal)) return false;
        current = candidate;
        return true;
    }
}
